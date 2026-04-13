using System.Diagnostics;
using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clustral.Sdk.Http;

/// <summary>
/// Catches unhandled exceptions and returns RFC 7807 Problem Details via
/// <see cref="ProblemDetailsWriter"/>.
///
/// This middleware is shared across every .NET service in Clustral
/// (ControlPlane, AuditService, ApiGateway) so unhandled exceptions produce
/// a consistent response body regardless of which service throws.
///
/// Development mode (<see cref="IHostEnvironment.IsDevelopment"/>) adds
/// <c>exception</c> and <c>stackTrace</c> extensions for debugging; those
/// are stripped in production.
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

            // Prefer the W3C-format trace ID (32-hex) over Activity.Id (full
            // W3C string "00-trace-span-flags") so the value we log and echo
            // matches what OpenTelemetry / Datadog / Honeycomb index by.
            var traceId = Activity.Current?.TraceId.ToString()
                          ?? Activity.Current?.Id
                          ?? context.TraceIdentifier;
            var (statusCode, problem) = ClassifyException(ex, context.Request, traceId);

            _logger.LogError(ex,
                "Unhandled {ExceptionType} in {Method} {Path} — TraceId: {TraceId}",
                ex.GetType().Name, context.Request.Method, context.Request.Path, traceId);

            // Path-aware body: /api/proxy/* must stay plain-text so kubectl's
            // aggregated-discovery client renders the message instead of
            // falling back to "unknown". Everything else gets RFC 7807.
            if (IsProxyPath(context.Request.Path))
            {
                var code = problem.Extensions.TryGetValue("code", out var c) && c is string s ? s : "INTERNAL_ERROR";
                var message = problem.Detail ?? "Internal error (see X-Correlation-Id for support).";
                await PlainTextErrorWriter.WriteAsync(context, statusCode, code, message);
                return;
            }

            if (_env.IsDevelopment())
            {
                problem.Extensions["exception"] = ex.GetType().FullName;
                problem.Extensions["stackTrace"] = ex.StackTrace;
            }

            var correlationId = ProblemDetailsWriter.EnsureCorrelationId(context);
            if (!problem.Extensions.ContainsKey("traceId"))
                problem.Extensions["traceId"] = correlationId;

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null,
                ProblemDetailsWriter.ProblemJsonContentType, context.RequestAborted);
        }
    }

    /// <summary>
    /// The kubectl-facing proxy path. Kept in sync with <c>GatewayErrorWriter.IsProxyPath</c>
    /// — both must use the same rule so the body shape stays consistent across
    /// success, domain errors, and unhandled exceptions.
    /// </summary>
    private static bool IsProxyPath(PathString path) =>
        path.StartsWithSegments("/api/proxy", StringComparison.OrdinalIgnoreCase);

    internal static (int StatusCode, ProblemDetails Problem) ClassifyException(
        Exception ex, HttpRequest request, string traceId)
    {
        var (statusCode, title, code) = ex switch
        {
            ResultFailureException rfe => (
                rfe.Error.Kind.ToHttpStatusCode(),
                rfe.Error.Kind.ToString(),
                rfe.Error.Code),

            OperationCanceledException => (499, "Client Closed Request", "CLIENT_CLOSED"),
            TimeoutException           => (504, "Gateway Timeout", "TIMEOUT"),
            UnauthorizedAccessException => (401, "Unauthorized", "UNAUTHORIZED"),
            ArgumentException          => (400, "Bad Request", "BAD_REQUEST"),
            InvalidOperationException  => (422, "Unprocessable Entity", "UNPROCESSABLE"),
            _                          => (500, "Internal Server Error", "INTERNAL_ERROR"),
        };
        var typeUri = ErrorDocumentation.UrlFor(code);

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
