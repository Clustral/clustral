using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Clustral.Sdk.Http;

/// <summary>
/// Cross-service request correlation middleware. Reads the caller's W3C
/// Trace Context (<c>traceparent</c>), falls back to <c>X-Correlation-Id</c>,
/// and finally generates a fresh ID if neither is present. Whichever ID is
/// chosen is echoed on the response, exposed in logs, and used by
/// downstream writers (error bodies, audit events) as the canonical request
/// identifier.
///
/// <para><b>W3C Trace Context integration.</b> ASP.NET Core's hosting
/// diagnostics (enabled by default since .NET 5) already parses an incoming
/// <c>traceparent</c> header and starts an <see cref="Activity"/> whose
/// <see cref="Activity.TraceId"/> equals the caller's trace ID. We honor
/// that trace ID over a caller-supplied <c>X-Correlation-Id</c> so that any
/// distributed-tracing backend (OpenTelemetry / Datadog / Honeycomb /
/// X-Ray / Elastic APM) plugged into the caller's pipeline automatically
/// correlates with Clustral logs without any extra configuration. A
/// W3C-formatted <c>traceresponse</c> header is returned for
/// W3C-Trace-Context-aware clients that want to retrieve the trace ID from
/// the response (e.g., a CLI that didn't start its own trace).
///
/// <para>Register first in the middleware pipeline so the exception
/// handler, auth, and request logging all share the same ID.</para>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>W3C Trace Context response header, format
    /// <c>00-{traceId}-{spanId}-{flags}</c>. Proposed by the W3C trace
    /// context group; increasingly honored by tracing clients.</summary>
    public const string TraceResponseHeader = "traceresponse";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Activity.Current is set by Microsoft.AspNetCore.Hosting.Diagnostics
        // before any middleware runs. If the caller sent traceparent, the
        // trace ID here equals the caller's; otherwise ASP.NET generated a
        // fresh W3C-format ID.
        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString();

        // Correlation ID precedence:
        //   1. Caller's X-Correlation-Id (backward compatibility)
        //   2. W3C trace ID (enterprise tracing integration)
        //   3. Fresh random GUID (fallback — shouldn't happen in practice)
        var id = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            id = traceId ?? Guid.NewGuid().ToString("N");

        // Normalize the request header so anything downstream that reads it
        // sees the chosen ID.
        context.Request.Headers[CorrelationHeader] = id;
        context.Response.Headers[CorrelationHeader] = id;

        // Emit traceresponse when we have an Activity and the caller didn't
        // already override the response. This lets W3C-aware clients recover
        // the trace ID without coupling to Clustral-specific headers.
        if (activity is not null &&
            !context.Response.Headers.ContainsKey(TraceResponseHeader))
        {
            var flags = ((int)activity.ActivityTraceFlags).ToString("x2");
            context.Response.Headers[TraceResponseHeader] =
                $"00-{activity.TraceId}-{activity.SpanId}-{flags}";
        }

        using (LogContext.PushProperty("CorrelationId", id))
        using (LogContext.PushProperty("TraceId", traceId ?? id))
        using (LogContext.PushProperty("SpanId", activity?.SpanId.ToString() ?? ""))
        {
            await _next(context);
        }
    }
}
