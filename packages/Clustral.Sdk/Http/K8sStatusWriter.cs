using System.Text.Json;
using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Http;

namespace Clustral.Sdk.Http;

/// <summary>
/// Writes Kubernetes <c>v1.Status</c> JSON error bodies for the
/// <c>/api/proxy/*</c> path.
///
/// <b>Why this exists (do not "simplify" to RFC 7807):</b>
/// The proxy path is semantically a Kubernetes API surface — kubectl,
/// client-go, operators, and k8s-ecosystem tools expect <c>v1.Status</c>
/// on every k8s endpoint and natively render <see cref="K8sStatus.Message"/>
/// as the single-line error the user sees (<c>"error: &lt;message&gt;"</c>).
/// Every managed-Kubernetes vendor (GKE/EKS/AKS/OpenShift/Rancher) follows
/// this convention. Returning RFC 7807 here would force kubectl to display
/// a raw JSON blob instead.
///
/// See the "Error Response Shapes" section of the root README for the full
/// rationale. For the general-purpose REST API use
/// <see cref="ProblemDetailsWriter"/> or <c>ToActionResult()</c> instead.
/// </summary>
public static class K8sStatusWriter
{
    public const string JsonContentType = "application/json";

    /// <summary>
    /// Writes a <c>v1.Status</c> response derived from a
    /// <see cref="ResultError"/>. Status code and <see cref="K8sStatus.Reason"/>
    /// are mapped from <see cref="ResultError.Kind"/>;
    /// <see cref="K8sStatus.Message"/> is the human-readable
    /// <see cref="ResultError.Message"/>; <see cref="ResultError.Code"/> goes
    /// into <c>details.causes[0].reason</c> for programmatic consumers.
    /// </summary>
    public static Task WriteAsync(HttpContext context, ResultError error)
    {
        var statusCode = MapStatusCode(error);
        var status = new K8sStatus
        {
            Status  = "Failure",
            Message = error.Message,
            Reason  = MapReason(error),
            Code    = statusCode,
            Details = new K8sStatusDetails
            {
                Group  = "clustral.io",
                Kind   = "proxy",
                Causes =
                [
                    new K8sStatusCause
                    {
                        Reason  = error.Code,
                        Message = error.Message,
                        Field   = error.Field,
                    },
                ],
            },
        };

        return WriteInternalAsync(context, statusCode, status);
    }

    /// <summary>
    /// Writes a <c>v1.Status</c> response with explicit status / reason /
    /// message / cause. Used from error paths that don't flow through
    /// <see cref="Result{T}"/> — e.g., gateway <c>OnChallenge</c>.
    /// </summary>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string reason,
        string message,
        string causeReason)
    {
        var status = new K8sStatus
        {
            Status  = "Failure",
            Message = message,
            Reason  = reason,
            Code    = statusCode,
            Details = new K8sStatusDetails
            {
                Group  = "clustral.io",
                Kind   = "proxy",
                Causes =
                [
                    new K8sStatusCause { Reason = causeReason, Message = message },
                ],
            },
        };

        return WriteInternalAsync(context, statusCode, status);
    }

    // ─── internal ────────────────────────────────────────────────────────

    private static async Task WriteInternalAsync(HttpContext context, int statusCode, K8sStatus status)
    {
        if (context.Response.HasStarted) return;

        // Always echo X-Correlation-Id so operators can cross-reference logs
        // regardless of which shape the body uses.
        ProblemDetailsWriter.EnsureCorrelationId(context);

        context.Response.StatusCode = statusCode;
        // Pass contentType to WriteAsJsonAsync so it doesn't override with the default "application/json; charset=utf-8".
        await context.Response.WriteAsJsonAsync(status, (JsonSerializerOptions?)null,
            JsonContentType, context.RequestAborted);
    }

    /// <summary>
    /// Maps a <see cref="ResultError"/> to an HTTP status code for the
    /// proxy path. Distinct proxy-specific codes override the generic
    /// <see cref="ResultErrorKind"/> mapping (e.g., <c>TUNNEL_TIMEOUT</c>
    /// uses 504 instead of Internal's 500).
    /// </summary>
    public static int MapStatusCode(ResultError error) => error.Code switch
    {
        "TUNNEL_TIMEOUT"      => 504,
        "AGENT_NOT_CONNECTED" => 502,
        "AGENT_ERROR"         => 502,
        "TUNNEL_ERROR"        => 502,
        _                     => error.Kind.ToHttpStatusCode(),
    };

    private static string MapReason(ResultError error)
    {
        // Proxy-specific codes map to k8s reason strings that kubectl's
        // error formatter understands cleanly.
        return error.Code switch
        {
            "TUNNEL_TIMEOUT"         => "Timeout",
            "AGENT_NOT_CONNECTED"    => "ServiceUnavailable",
            "AGENT_ERROR"            => "ServiceUnavailable",
            "TUNNEL_ERROR"           => "ServiceUnavailable",
            "INVALID_CLUSTER_ID"     => "BadRequest",
            _ => error.Kind switch
            {
                ResultErrorKind.NotFound     => "NotFound",
                ResultErrorKind.Unauthorized => "Unauthorized",
                ResultErrorKind.Forbidden    => "Forbidden",
                ResultErrorKind.Conflict     => "AlreadyExists",
                ResultErrorKind.BadRequest   => "BadRequest",
                ResultErrorKind.Validation   => "Invalid",
                ResultErrorKind.Internal     => "InternalError",
                _                            => "InternalError",
            },
        };
    }
}
