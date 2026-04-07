using Clustral.ControlPlane.Domain.Events;
using Clustral.Sdk.Results;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// Aggregate root for Just-In-Time access requests. A user asks for temporary
/// elevated access to a cluster with a specific role. An admin approves or denies.
/// The request carries the full grant lifecycle so the audit trail lives in one document.
/// </summary>
public sealed class AccessRequest : HasDomainEvents
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

    // ── Revocation fields ────────────────────────────────────────────────

    /// <summary>When the grant was revoked (before natural expiry).</summary>
    [BsonIgnoreIfNull]
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Who revoked the grant.</summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? RevokedBy { get; set; }

    /// <summary>Reason for revocation.</summary>
    [BsonIgnoreIfNull]
    public string? RevokedReason { get; set; }

    // ── Computed helpers ─────────────────────────────────────────────────

    [BsonIgnore]
    public bool IsPendingExpired =>
        Status == AccessRequestStatus.Pending &&
        DateTimeOffset.UtcNow >= RequestExpiresAt;

    [BsonIgnore]
    public bool IsRevoked => RevokedAt.HasValue;

    [BsonIgnore]
    public bool IsGrantActive =>
        Status == AccessRequestStatus.Approved &&
        GrantExpiresAt.HasValue &&
        DateTimeOffset.UtcNow < GrantExpiresAt.Value &&
        !RevokedAt.HasValue;

    // ── Aggregate methods (state transitions) ────────────────────────────

    /// <summary>
    /// Creates a new pending access request.
    /// </summary>
    public static AccessRequest Create(
        Guid requesterId, Guid roleId, Guid clusterId,
        string? reason, TimeSpan requestedDuration, TimeSpan requestTtl,
        List<Guid>? suggestedReviewers = null)
    {
        var ar = new AccessRequest
        {
            Id = Guid.NewGuid(),
            RequesterId = requesterId,
            RoleId = roleId,
            ClusterId = clusterId,
            Reason = reason ?? string.Empty,
            RequestedDuration = requestedDuration,
            CreatedAt = DateTimeOffset.UtcNow,
            RequestExpiresAt = DateTimeOffset.UtcNow + requestTtl,
            SuggestedReviewers = suggestedReviewers ?? [],
        };
        ar.RaiseDomainEvent(new AccessRequestCreated(
            ar.Id, requesterId, roleId, clusterId, reason, requestedDuration));
        return ar;
    }

    /// <summary>
    /// Approves a pending request, granting access for the specified duration.
    /// </summary>
    public Result Approve(Guid reviewerId, TimeSpan grantDuration)
    {
        if (Status != AccessRequestStatus.Pending)
            return ResultErrors.RequestNotPending(Status.ToString());
        if (IsPendingExpired)
            return ResultErrors.RequestExpired();

        var now = DateTimeOffset.UtcNow;
        Status = AccessRequestStatus.Approved;
        ReviewerId = reviewerId;
        ReviewedAt = now;
        GrantExpiresAt = now + grantDuration;
        RaiseDomainEvent(new AccessRequestApproved(Id, reviewerId, grantDuration, GrantExpiresAt.Value));
        return Result.Success();
    }

    /// <summary>
    /// Denies a pending request with a reason.
    /// </summary>
    public Result Deny(Guid reviewerId, string reason)
    {
        if (Status != AccessRequestStatus.Pending)
            return ResultErrors.RequestNotPending(Status.ToString());

        Status = AccessRequestStatus.Denied;
        ReviewerId = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        DenialReason = reason;
        RaiseDomainEvent(new AccessRequestDenied(Id, reviewerId, reason));
        return Result.Success();
    }

    /// <summary>
    /// Revokes an active approved grant before its natural expiry.
    /// </summary>
    public Result Revoke(Guid revokedById, string? reason)
    {
        if (Status != AccessRequestStatus.Approved)
            return ResultErrors.GrantNotApproved(Status.ToString());
        if (IsRevoked)
            return ResultErrors.GrantAlreadyRevoked();
        if (!IsGrantActive)
            return ResultErrors.GrantAlreadyExpired();

        Status = AccessRequestStatus.Revoked;
        RevokedAt = DateTimeOffset.UtcNow;
        RevokedBy = revokedById;
        RevokedReason = reason;
        RaiseDomainEvent(new AccessRequestRevoked(Id, revokedById, reason));
        return Result.Success();
    }

    /// <summary>
    /// Expires a pending request or an approved grant that has passed its TTL.
    /// Called by the background cleanup service.
    /// </summary>
    public Result Expire()
    {
        if (Status == AccessRequestStatus.Pending && DateTimeOffset.UtcNow >= RequestExpiresAt)
        {
            Status = AccessRequestStatus.Expired;
            return Result.Success();
        }

        if (Status == AccessRequestStatus.Approved && GrantExpiresAt.HasValue
            && DateTimeOffset.UtcNow >= GrantExpiresAt.Value && !RevokedAt.HasValue)
        {
            Status = AccessRequestStatus.Expired;
            return Result.Success();
        }

        return Result.Success(); // Nothing to expire — no-op is fine.
    }
}

public enum AccessRequestStatus
{
    Pending  = 0,
    Approved = 1,
    Denied   = 2,
    Expired  = 3,
    Revoked  = 4,
}
