using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Clustral.Sdk.Http;

/// <summary>
/// Reads or generates an <c>X-Correlation-Id</c> header, echoes it on the
/// response, and pushes it to Serilog's <see cref="LogContext"/> for the
/// duration of the request so every log line carries it.
///
/// Register first in the middleware pipeline so downstream middleware
/// (exception handler, auth, request logging) all see the same ID.
///
/// Clustral services emit this header on <i>every</i> HTTP response —
/// success or failure — so clients can reference it in support tickets
/// and operators can grep all three services (gateway, ControlPlane,
/// AuditService) for a single request.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            id = Guid.NewGuid().ToString("N");

        // Normalize so anything downstream reading the request header gets it.
        context.Request.Headers[HeaderName] = id;
        context.Response.Headers[HeaderName] = id;

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await _next(context);
        }
    }
}
