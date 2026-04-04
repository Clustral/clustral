using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// A short-lived (user kubeconfig) or long-lived (agent) Clustral bearer token.
/// The raw token is never stored; only the SHA-256 hash is persisted.
/// </summary>
public sealed class AccessToken
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public CredentialKind Kind { get; set; }

    /// <summary>
    /// SHA-256 hex of the raw bearer token.  Used for O(1) validation lookups.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Cluster this credential is scoped to.</summary>
    [BsonRepresentation(BsonType.String)]
    public Guid ClusterId { get; set; }

    /// <summary>
    /// Owning user. <c>null</c> for agent credentials
    /// (<see cref="CredentialKind.Agent"/>).
    /// </summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? UserId { get; set; }

    public DateTimeOffset  IssuedAt  { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset  ExpiresAt { get; set; }

    [BsonIgnoreIfNull]
    public DateTimeOffset? RevokedAt { get; set; }

    [BsonIgnoreIfNull]
    public string? RevokedReason { get; set; }

    [BsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    [BsonIgnore]
    public bool IsRevoked => RevokedAt.HasValue;

    [BsonIgnore]
    public bool IsValid   => !IsExpired && !IsRevoked;
}

public enum CredentialKind
{
    UserKubeconfig = 0,
    Agent          = 1,
}
