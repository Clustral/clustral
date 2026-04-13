using Clustral.Sdk.Http;

namespace Clustral.ApiGateway.Api;

/// <summary>
/// Path-aware error-body writer for the API Gateway.
///
/// <b>Why path-aware:</b> kubectl (and every k8s-ecosystem client) hits
/// <c>/api/proxy/*</c> and expects a body that its client-go discovery
/// layer can process. kubectl's aggregated-discovery client doesn't
/// register <c>metav1.Status</c> in its runtime scheme, so JSON bodies
/// fail to decode and kubectl falls back to the hardcoded string
/// <c>"unknown"</c>. Plain text triggers client-go's <c>isTextResponse</c>
/// branch and the user sees our actual message.
///
/// All other paths are used by general HTTP clients (Web UI, CLI,
/// integrators) that expect RFC 7807 Problem Details.
///
/// See the "Error Response Shapes" section of the root README for the full
/// rationale, alternatives considered, and trade-offs.
/// </summary>
public static class GatewayErrorWriter
{
    /// <summary>
    /// Writes an error body in the shape appropriate for the request path.
    /// Echoes <c>X-Correlation-Id</c> on the response, and (for the proxy
    /// path) puts the Clustral error code in <c>X-Clustral-Error-Code</c>.
    /// </summary>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        if (IsProxyPath(context.Request.Path))
            return PlainTextErrorWriter.WriteAsync(context, statusCode, code, message);

        return ProblemDetailsWriter.WriteAsync(context, statusCode, code, message);
    }

    public static bool IsProxyPath(PathString path) =>
        path.StartsWithSegments("/api/proxy", StringComparison.OrdinalIgnoreCase);
}
