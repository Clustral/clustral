using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// A human user who has authenticated through an OIDC provider at least once.
/// Created or updated via <see cref="Services.UserSyncService"/>.
/// </summary>
public sealed class User
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>The OIDC <c>sub</c> claim — globally unique and stable.</summary>
    public string KeycloakSubject { get; set; } = string.Empty;

    [BsonIgnoreIfNull]
    public string? DisplayName { get; set; }

    [BsonIgnoreIfNull]
    public string? Email { get; set; }

    public DateTimeOffset  CreatedAt  { get; set; } = DateTimeOffset.UtcNow;

    [BsonIgnoreIfNull]
    public DateTimeOffset? LastSeenAt { get; set; }

    // ── Domain methods ───────────────────────────────────────────────────

    /// <summary>
    /// Updates the user's profile from OIDC claims. Called on every
    /// authenticated request to keep display name and email in sync.
    /// </summary>
    public void UpdateFromOidcClaims(string? email, string? displayName)
    {
        Email = email;
        DisplayName = displayName;
        LastSeenAt = DateTimeOffset.UtcNow;
    }
}
