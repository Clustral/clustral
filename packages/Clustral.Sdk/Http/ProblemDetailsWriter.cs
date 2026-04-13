using System.Diagnostics;
using System.Text.Json;
using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.Sdk.Http;

/// <summary>
/// Writes RFC 7807 <c>application/problem+json</c> responses.
///
/// This is the general-purpose error body writer used for every Clustral
/// HTTP surface EXCEPT the kubectl proxy path (<c>/api/proxy/*</c>) — that
/// path uses <see cref="PlainTextErrorWriter"/> because kubectl's
/// aggregated-discovery client can't decode JSON error bodies and falls
/// back to a hardcoded "unknown" message. See the "Error Response Shapes"
/// section in the root README and <c>docs/adr/001-error-response-shapes.md</c>
/// for the full rationale behind the path-aware split.
///
/// Produces the same JSON shape as <see cref="ResultExtensions.ToActionResult"/>
/// so MVC actions and middleware return byte-identical bodies. Every
/// response echoes <c>X-Correlation-Id</c> so operators can cross-reference
/// logs.
/// </summary>
public static class ProblemDetailsWriter
{
    private const string CorrelationHeader = "X-Correlation-Id";
    public const string ProblemJsonContentType = "application/problem+json";

    /// <summary>
    /// Writes an RFC 7807 response built from a <see cref="ResultError"/>.
    /// Status code is derived from <see cref="ResultError.Kind"/>.
    /// </summary>
    public static Task WriteAsync(HttpContext context, ResultError error)
    {
        var problem = BuildProblem(error, context.Request.Path);
        return WriteInternalAsync(context, error.Kind.ToHttpStatusCode(), problem);
    }

    /// <summary>
    /// Writes an RFC 7807 response with explicit status / code / message.
    /// Used for error paths that don't flow through the <see cref="Result{T}"/>
    /// pipeline (e.g., gateway <c>OnChallenge</c>).
    /// </summary>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        string? field = null)
    {
        var problem = new ProblemDetails
        {
            Type     = ErrorDocumentation.UrlFor(code),
            Title    = TitleFor(statusCode),
            Status   = statusCode,
            Detail   = message,
            Instance = context.Request.Path,
        };
        problem.Extensions["code"] = code;
        if (field is not null) problem.Extensions["field"] = field;

        return WriteInternalAsync(context, statusCode, problem);
    }

    /// <summary>
    /// Builds a <see cref="ProblemDetails"/> from a <see cref="ResultError"/>
    /// including every Clustral-standard extension (code, field, metadata,
    /// traceId). Exposed so <see cref="ResultExtensions.ToActionResult"/>
    /// can share the exact shape.
    /// </summary>
    public static ProblemDetails BuildProblem(ResultError error, string? instance = null)
    {
        var statusCode = error.Kind.ToHttpStatusCode();
        var problem = new ProblemDetails
        {
            Type     = ErrorDocumentation.UrlFor(error.Code),
            Title    = error.Kind.ToString(),
            Status   = statusCode,
            Detail   = error.Message,
            Instance = instance,
        };

        problem.Extensions["code"] = error.Code;
        if (error.Field is not null) problem.Extensions["field"] = error.Field;
        if (error.TraceId is not null) problem.Extensions["traceId"] = error.TraceId;
        if (error.Metadata is not null)
        {
            foreach (var kv in error.Metadata)
                problem.Extensions[kv.Key] = kv.Value;
        }

        return problem;
    }

    /// <summary>
    /// Ensures the current <see cref="HttpContext"/> has an
    /// <c>X-Correlation-Id</c> header (reads incoming header or generates
    /// a fresh GUID) and echoes it on the response. Returns the value.
    /// </summary>
    public static string EnsureCorrelationId(HttpContext context)
    {
        var id = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            id = Guid.NewGuid().ToString("N");
        context.Response.Headers[CorrelationHeader] = id;
        return id;
    }

    // ─── internal ────────────────────────────────────────────────────────

    private static async Task WriteInternalAsync(HttpContext context, int statusCode, ProblemDetails problem)
    {
        if (context.Response.HasStarted) return;

        var correlationId = EnsureCorrelationId(context);
        if (!problem.Extensions.ContainsKey("traceId"))
        {
            // Prefer the W3C trace ID over our correlation GUID when Activity
            // is present — the W3C trace ID is the enterprise-interop key
            // that a caller's Datadog/OTel/X-Ray pipeline already knows.
            // Falls back to correlation ID if no Activity is active.
            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? correlationId;
        }

        context.Response.StatusCode = statusCode;
        // Pass contentType to WriteAsJsonAsync so it doesn't override with the default "application/json; charset=utf-8".
        await context.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null,
            ProblemJsonContentType, context.RequestAborted);
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        499 => "Client Closed Request",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _   => "Error",
    };
}
