using System.Diagnostics;
using System.Text.Json;
using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.ControlPlane.Api;

/// <summary>
/// Global exception handler that catches unhandled exceptions and returns
/// RFC 7807 Problem Details JSON responses. Environment-aware: development
/// mode includes exception details and stack traces, production does not.
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(ex, "Response already started — cannot write error body");
                return;
            }

            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            var (statusCode, problem) = ClassifyException(ex, context.Request, traceId);

            _logger.LogError(ex,
                "Unhandled {ExceptionType} in {Method} {Path} — TraceId: {TraceId}",
                ex.GetType().Name, context.Request.Method, context.Request.Path, traceId);

            if (_env.IsDevelopment())
            {
                problem.Extensions["exception"] = ex.GetType().FullName;
                problem.Extensions["stackTrace"] = ex.StackTrace;
            }

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, context.RequestAborted);
        }
    }

    internal static (int StatusCode, ProblemDetails Problem) ClassifyException(
        Exception ex, HttpRequest request, string traceId)
    {
        var (statusCode, title, typeUri) = ex switch
        {
            ResultFailureException rfe => (
                rfe.Error.Kind.ToHttpStatusCode(),
                rfe.Error.Kind.ToString(),
                $"urn:clustral:error:{rfe.Error.Code.ToLowerInvariant()}"),

            OperationCanceledException => (499, "Client Closed Request", "urn:clustral:error:client_closed"),
            TimeoutException           => (504, "Gateway Timeout", "urn:clustral:error:timeout"),
            UnauthorizedAccessException => (401, "Unauthorized", "urn:clustral:error:unauthorized"),
            ArgumentException          => (400, "Bad Request", "urn:clustral:error:bad_request"),
            InvalidOperationException  => (422, "Unprocessable Entity", "urn:clustral:error:unprocessable"),
            _                          => (500, "Internal Server Error", "urn:clustral:error:internal"),
        };

        var detail = ex is ResultFailureException r ? r.Error.Message : ex.Message;

        // Never expose internal details in production for 5xx errors.
        if (statusCode >= 500 && ex is not ResultFailureException)
            detail = "An internal error occurred. Check server logs for details.";

        var problem = new ProblemDetails
        {
            Type     = typeUri,
            Title    = title,
            Status   = statusCode,
            Detail   = detail,
            Instance = request.Path,
        };

        problem.Extensions["traceId"] = traceId;

        if (ex is ResultFailureException rfe2)
        {
            problem.Extensions["code"] = rfe2.Error.Code;
            if (rfe2.Error.Field is not null)
                problem.Extensions["field"] = rfe2.Error.Field;
            if (rfe2.Error.Metadata is not null)
            {
                foreach (var kv in rfe2.Error.Metadata)
                    problem.Extensions[kv.Key] = kv.Value;
            }
        }

        return (statusCode, problem);
    }
}
