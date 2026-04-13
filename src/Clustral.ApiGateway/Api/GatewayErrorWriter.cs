using Clustral.Sdk.Http;

namespace Clustral.ApiGateway.Api;

/// <summary>
/// Path-aware error-body writer for the API Gateway.
///
/// <b>Why path-aware:</b> kubectl connects to <c>/api/proxy/*</c> and expects
/// Kubernetes <c>v1.Status</c> error bodies (it renders
/// <c>Message</c> as <c>"error: &lt;message&gt;"</c>). All other paths are
/// used by general HTTP clients (Web UI, CLI, integrators) that expect
/// RFC 7807 Problem Details. Returning one shape to both audiences is
/// worse for either one; returning each audience's own shape is what
/// GKE/EKS/AKS do.
///
/// See the "Error Response Shapes" section of the root README for the full
/// rationale, alternatives considered, and the canonical error-code table.
/// </summary>
public static class GatewayErrorWriter
{
    /// <summary>
    /// Writes an error body in the shape appropriate for the request path.
    /// Echoes <c>X-Correlation-Id</c> on the response.
    /// </summary>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        if (IsProxyPath(context.Request.Path))
        {
            return K8sStatusWriter.WriteAsync(context, statusCode,
                reason: K8sReasonFor(statusCode),
                message: message,
                causeReason: code);
        }

        return ProblemDetailsWriter.WriteAsync(context, statusCode, code, message);
    }

    public static bool IsProxyPath(PathString path) =>
        path.StartsWithSegments("/api/proxy", StringComparison.OrdinalIgnoreCase);

    private static string K8sReasonFor(int statusCode) => statusCode switch
    {
        400 => "BadRequest",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "NotFound",
        409 => "AlreadyExists",
        422 => "Invalid",
        429 => "TooManyRequests",
        500 => "InternalError",
        502 => "ServiceUnavailable",
        503 => "ServiceUnavailable",
        504 => "Timeout",
        _   => "InternalError",
    };
}
