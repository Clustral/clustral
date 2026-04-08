using Clustral.ControlPlane.Domain.Events;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// Aggregate root for a Kubernetes cluster registered with the Clustral control plane.
/// </summary>
public sealed class Cluster : HasDomainEvents
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

    /// <summary>Version reported by the agent in <c>AgentHello</c>.</summary>
    [BsonIgnoreIfNull]
    public string?       AgentVersion      { get; set; }

    [BsonRepresentation(BsonType.String)]
    public ClusterStatus Status            { get; set; } = ClusterStatus.Pending;

    public DateTimeOffset  RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    [BsonIgnoreIfNull]
    public DateTimeOffset? LastSeenAt   { get; set; }

    public Dictionary<string, string> Labels { get; set; } = new();

    /// <summary>
    /// Token version for JWT revocation. Incremented only on explicit revocation,
    /// not on renewal. Agents with a JWT whose token_version &lt; this are rejected.
    /// </summary>
    public int TokenVersion { get; set; } = 1;

    /// <summary>
    /// SHA-256 fingerprint of the most recently issued agent client certificate.
    /// </summary>
    [BsonIgnoreIfNull]
    public string? CertificateFingerprint { get; set; }

    // ── Aggregate methods ────────────────────────────────────────────────

    /// <summary>
    /// Creates a new cluster in Pending state with a bootstrap token hash.
    /// </summary>
    public static Cluster Create(
        string name, string description, string agentPublicKeyPem,
        string bootstrapTokenHash, Dictionary<string, string>? labels = null)
    {
        var cluster = new Cluster
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            AgentPublicKeyPem = agentPublicKeyPem,
            BootstrapTokenHash = bootstrapTokenHash,
            Status = ClusterStatus.Pending,
            Labels = labels ?? new Dictionary<string, string>(),
        };
        cluster.RaiseDomainEvent(new ClusterRegistered(cluster.Id, name));
        return cluster;
    }

    /// <summary>
    /// Marks the cluster as connected (agent tunnel established).
    /// </summary>
    public void Connect(string? kubernetesVersion = null, string? agentVersion = null)
    {
        Status = ClusterStatus.Connected;
        LastSeenAt = DateTimeOffset.UtcNow;
        if (kubernetesVersion is not null)
            KubernetesVersion = kubernetesVersion;
        if (agentVersion is not null)
            AgentVersion = agentVersion;
        RaiseDomainEvent(new ClusterConnected(Id, kubernetesVersion));
    }

    /// <summary>
    /// Marks the cluster as disconnected (agent tunnel lost).
    /// </summary>
    public void Disconnect()
    {
        Status = ClusterStatus.Disconnected;
        RaiseDomainEvent(new ClusterDisconnected(Id));
    }

    /// <summary>
    /// Updates the last-seen timestamp from an agent heartbeat.
    /// </summary>
    public void RecordHeartbeat(string? kubernetesVersion = null)
    {
        LastSeenAt = DateTimeOffset.UtcNow;
        if (kubernetesVersion is not null)
            KubernetesVersion = kubernetesVersion;
    }

    /// <summary>
    /// Consumes the bootstrap token (clears the hash). Called once when
    /// the agent exchanges the bootstrap token for a credential.
    /// </summary>
    public void ConsumeBootstrapToken()
    {
        BootstrapTokenHash = null;
    }

    /// <summary>
    /// Revokes all agent credentials by incrementing the token version.
    /// All JWTs with a lower version become invalid immediately.
    /// </summary>
    public void RevokeAgentCredentials()
    {
        TokenVersion++;
        RaiseDomainEvent(new AgentCredentialsRevoked(Id, TokenVersion));
    }

    /// <summary>
    /// Records the fingerprint of the most recently issued client certificate.
    /// </summary>
    public void RecordCertificateFingerprint(string fingerprint)
    {
        CertificateFingerprint = fingerprint;
    }
}

public enum ClusterStatus
{
    Pending      = 0,
    Connected    = 1,
    Disconnected = 2,
}
