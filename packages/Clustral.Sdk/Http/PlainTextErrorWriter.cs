using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Http;

namespace Clustral.Sdk.Http;

/// <summary>
/// Writes plain-text error bodies for the <c>/api/proxy/*</c> path.
///
/// <b>Why plain text (not RFC 7807, not v1.Status):</b> kubectl's aggregated-
/// discovery client (v1.30+) uses a <see cref="Microsoft"/>-style custom
/// runtime scheme that does NOT register <c>metav1.Status</c>. When it
/// receives a <c>v1.Status</c> JSON body on a 4xx/5xx response, the decode
/// fails and client-go falls back to its hardcoded default message
/// <c>"unknown"</c> (see <c>rest/request.go: newUnstructuredResponseError</c>).
/// Real k8s clusters never hit this path because they allow anonymous
/// discovery on <c>/api</c>, so the limitation is untested in upstream.
///
/// Plain-text bodies work correctly: client-go's
/// <c>isTextResponse(resp)</c> is true for <c>text/plain</c>, and the
/// fallback then uses <c>message = strings.TrimSpace(body)</c> — the
/// user sees our actual error message.
///
/// The Clustral-specific error code rides in the <c>X-Clustral-Error-Code</c>
/// response header so programmatic clients can still switch on it without
/// parsing the body.
/// </summary>
public static class PlainTextErrorWriter
{
    public const string ErrorCodeHeader = "X-Clustral-Error-Code";
    public const string ErrorFieldHeader = "X-Clustral-Error-Field";

    /// <summary>
    /// Prefix for <see cref="ResultError.Metadata"/> entries. Each key becomes
    /// a header of the form <c>X-Clustral-Error-Meta-{Key}</c>.
    /// Programmatic clients get structured error details (cluster IDs, user
    /// emails, timestamps, etc.) without having to parse the message text.
    /// </summary>
    public const string MetadataHeaderPrefix = "X-Clustral-Error-Meta-";

    public const string ContentType = "text/plain; charset=utf-8";

    /// <summary>
    /// Writes a plain-text error body derived from a <see cref="ResultError"/>.
    /// Surfaces every structured field from <see cref="ResultError"/> as a
    /// response header so programmatic clients have the same detail they'd
    /// get from an RFC 7807 Problem Details body:
    /// <list type="bullet">
    ///   <item><c>X-Clustral-Error-Code</c> — machine-readable code</item>
    ///   <item><c>X-Clustral-Error-Field</c> — field name for validation errors</item>
    ///   <item><c>X-Clustral-Error-Meta-{Key}</c> — one per metadata entry</item>
    ///   <item><c>X-Correlation-Id</c> — echoed (via <see cref="ProblemDetailsWriter.EnsureCorrelationId"/>)</item>
    /// </list>
    /// Status code is derived from proxy-specific <see cref="ResultError.Code"/>
    /// overrides (<c>TUNNEL_TIMEOUT</c> → 504, <c>AGENT_*</c> → 502) falling
    /// back to <see cref="ResultError.Kind"/>'s standard HTTP mapping.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, ResultError error)
    {
        if (context.Response.HasStarted) return;

        ProblemDetailsWriter.EnsureCorrelationId(context);
        context.Response.Headers[ErrorCodeHeader] = error.Code;
        SetDocumentationLink(context, error.Code);
        if (error.Field is not null)
            context.Response.Headers[ErrorFieldHeader] = error.Field;
        if (error.Metadata is not null)
        {
            foreach (var (key, value) in error.Metadata)
                context.Response.Headers[MetadataHeaderPrefix + key] = FormatMetadataValue(value);
        }

        context.Response.StatusCode = MapStatusCode(error);
        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(error.Message, context.RequestAborted);
    }

    /// <summary>
    /// Writes a plain-text error with explicit status / code / message.
    /// Used from error paths that don't flow through <see cref="Result{T}"/>
    /// — e.g., gateway <c>OnChallenge</c>.
    /// </summary>
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        if (context.Response.HasStarted) return;

        ProblemDetailsWriter.EnsureCorrelationId(context);
        context.Response.Headers[ErrorCodeHeader] = code;
        SetDocumentationLink(context, code);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(message, context.RequestAborted);
    }

    /// <summary>
    /// Sets the RFC 8288 <c>Link</c> header pointing at the error's
    /// documentation page, enabling any HTTP client (including terminal
    /// tools via <c>curl -I</c>) to discover docs from the response headers.
    /// </summary>
    private static void SetDocumentationLink(HttpContext context, string code)
    {
        var url = ErrorDocumentation.UrlFor(code);
        // RFC 8288: Link: <url>; rel="help"
        context.Response.Headers.Append("Link", $"<{url}>; rel=\"help\"");
    }

    /// <summary>
    /// Serializes a metadata value to a single header line. Handles the
    /// common cases produced by <see cref="ResultErrors"/> (string, Guid,
    /// int, DateTimeOffset, bool); falls back to <c>ToString()</c>.
    /// HTTP headers cannot contain CR/LF, so those are stripped.
    /// </summary>
    private static string FormatMetadataValue(object? value) => value switch
    {
        null                 => string.Empty,
        string s             => s.Replace('\n', ' ').Replace('\r', ' '),
        DateTimeOffset dto   => dto.ToString("O"),  // ISO 8601
        DateTime dt          => dt.ToString("O"),
        TimeSpan ts          => ts.ToString("c"),
        IFormattable f       => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _                    => value.ToString()?.Replace('\n', ' ').Replace('\r', ' ') ?? string.Empty,
    };

    /// <summary>
    /// Maps a <see cref="ResultError"/> to the proxy-path HTTP status code.
    /// Exposed so middleware (e.g., <c>KubectlProxyMiddleware</c>) can know
    /// the code ahead of writing.
    /// </summary>
    public static int MapStatusCode(ResultError error) => error.Code switch
    {
        "TUNNEL_TIMEOUT"      => 504,
        "AGENT_NOT_CONNECTED" => 502,
        "AGENT_ERROR"         => 502,
        "TUNNEL_ERROR"        => 502,
        _                     => error.Kind.ToHttpStatusCode(),
    };
}
