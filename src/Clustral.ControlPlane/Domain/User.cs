using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// A human user who has authenticated through Keycloak at least once.
/// Created or updated on first kubeconfig-credential issuance.
/// </summary>
public sealed class User
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>The Keycloak <c>sub</c> claim — globally unique and stable.</summary>
    public string KeycloakSubject { get; set; } = string.Empty;

    [BsonIgnoreIfNull]
    public string? DisplayName { get; set; }

    [BsonIgnoreIfNull]
    public string? Email { get; set; }

    public DateTimeOffset  CreatedAt  { get; set; } = DateTimeOffset.UtcNow;

    [BsonIgnoreIfNull]
    public DateTimeOffset? LastSeenAt { get; set; }
}
