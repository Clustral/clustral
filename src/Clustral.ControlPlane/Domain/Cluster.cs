using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// A Kubernetes cluster registered with the Clustral control plane.
/// Mirrors the <c>Cluster</c> proto message and is the central aggregate root.
/// </summary>
public sealed class Cluster
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public string Name               { get; set; } = string.Empty;
    public string Description        { get; set; } = string.Empty;

    /// <summary>PEM-encoded Ed25519 public key submitted at registration time.</summary>
    public string AgentPublicKeyPem  { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex of the one-time bootstrap token issued at registration.
    /// Cleared once the Agent calls <c>AuthService.IssueAgentCredential</c>.
    /// </summary>
    [BsonIgnoreIfNull]
    public string? BootstrapTokenHash { get; set; }

    [BsonIgnoreIfNull]
    public string?       KubernetesVersion { get; set; }

    [BsonRepresentation(BsonType.String)]
    public ClusterStatus Status            { get; set; } = ClusterStatus.Pending;

    public DateTimeOffset  RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    [BsonIgnoreIfNull]
    public DateTimeOffset? LastSeenAt   { get; set; }

    public Dictionary<string, string> Labels { get; set; } = new();
}

public enum ClusterStatus
{
    Pending      = 0,
    Connected    = 1,
    Disconnected = 2,
}
