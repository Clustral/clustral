using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// A Just-In-Time access request: a user asks for temporary elevated access
/// to a cluster with a specific role. An admin approves or denies.
/// The request itself carries the grant state (<see cref="GrantExpiresAt"/>)
/// so the full audit trail lives in one document.
/// </summary>
public sealed class AccessRequest
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>User who created the request.</summary>
    [BsonRepresentation(BsonType.String)]
    public Guid RequesterId { get; set; }

    /// <summary>Requested role.</summary>
    [BsonRepresentation(BsonType.String)]
    public Guid RoleId { get; set; }

    /// <summary>Target cluster.</summary>
    [BsonRepresentation(BsonType.String)]
    public Guid ClusterId { get; set; }

    /// <summary>Justification for the access request.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Current lifecycle state.</summary>
    [BsonRepresentation(BsonType.String)]
    public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;

    /// <summary>How long the access should last once approved.</summary>
    public TimeSpan RequestedDuration { get; set; } = TimeSpan.FromHours(8);

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Pending requests expire after this time (default +1h from creation).</summary>
    public DateTimeOffset RequestExpiresAt { get; set; }

    /// <summary>
    /// Suggested reviewers (user IDs). Advisory in v1 — any authenticated
    /// user can approve/deny. Highlighted in UI so the right people notice.
    /// </summary>
    public List<Guid> SuggestedReviewers { get; set; } = [];

    // ── Review fields ────────────────────────────────────────────────────

    /// <summary>User who approved or denied the request.</summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? ReviewerId { get; set; }

    [BsonIgnoreIfNull]
    public DateTimeOffset? ReviewedAt { get; set; }

    [BsonIgnoreIfNull]
    public string? DenialReason { get; set; }

    // ── Grant fields (populated on approval) ─────────────────────────────

    /// <summary>
    /// When the approved access expires. Set to <c>ReviewedAt + RequestedDuration</c>
    /// (or an admin-specified override) on approval.
    /// </summary>
    [BsonIgnoreIfNull]
    public DateTimeOffset? GrantExpiresAt { get; set; }

    // ── Computed helpers ─────────────────────────────────────────────────

    [BsonIgnore]
    public bool IsPendingExpired =>
        Status == AccessRequestStatus.Pending &&
        DateTimeOffset.UtcNow >= RequestExpiresAt;

    [BsonIgnore]
    public bool IsGrantActive =>
        Status == AccessRequestStatus.Approved &&
        GrantExpiresAt.HasValue &&
        DateTimeOffset.UtcNow < GrantExpiresAt.Value;
}

public enum AccessRequestStatus
{
    Pending  = 0,
    Approved = 1,
    Denied   = 2,
    Expired  = 3,
}
