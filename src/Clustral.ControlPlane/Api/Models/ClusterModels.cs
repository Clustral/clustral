using Clustral.ControlPlane.Domain;
namespace Clustral.ControlPlane.Api.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Request bodies
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// POST /api/v1/clusters — register a new cluster
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RegisterClusterRestRequest(
    string Name,
    string Description = "",
    string AgentPublicKeyPem = "",
    Dictionary<string, string>? Labels = null);

public sealed record RegisterClusterRestResponse(
    Guid   ClusterId,
    string BootstrapToken);

// ─────────────────────────────────────────────────────────────────────────────
// Response bodies
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ClusterResponse(
    Guid                    Id,
    string                  Name,
    string                  Description,
    string                  Status,
    string?                 KubernetesVersion,
    DateTimeOffset          RegisteredAt,
    DateTimeOffset?         LastSeenAt,
    Dictionary<string,string> Labels)
{
    public static ClusterResponse From(Cluster c) => new(
        c.Id,
        c.Name,
        c.Description,
        c.Status.ToString(),
        c.KubernetesVersion,
        c.RegisteredAt,
        c.LastSeenAt,
        c.Labels);
}

public sealed record ClusterListResponse(
    IReadOnlyList<ClusterResponse> Clusters,
    string?                        NextPageToken);
