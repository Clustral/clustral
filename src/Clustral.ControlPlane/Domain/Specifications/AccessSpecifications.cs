using Clustral.ControlPlane.Infrastructure;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Domain.Specifications;

/// <summary>
/// Reusable query predicates for access-related lookups.
/// Eliminates duplication across CreateAccessRequest, IssueKubeconfigCredential,
/// and ListAccessRequests handlers.
/// </summary>
public sealed class AccessSpecifications(ClustralDb db)
{
    /// <summary>
    /// Returns true if the user has a static role assignment for the given cluster.
    /// </summary>
    public async Task<bool> HasStaticAssignmentAsync(
        Guid userId, Guid clusterId, CancellationToken ct = default)
    {
        return await db.RoleAssignments
            .Find(a => a.UserId == userId && a.ClusterId == clusterId)
            .AnyAsync(ct);
    }

    /// <summary>
    /// Returns the pending access request for the user+cluster, or null.
    /// </summary>
    public async Task<AccessRequest?> GetPendingRequestAsync(
        Guid userId, Guid clusterId, CancellationToken ct = default)
    {
        return await db.AccessRequests
            .Find(r => r.RequesterId == userId
                     && r.ClusterId == clusterId
                     && r.Status == AccessRequestStatus.Pending)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Returns the active approved grant (not expired, not revoked) for the
    /// user+cluster, or null.
    /// </summary>
    public async Task<AccessRequest?> GetActiveGrantAsync(
        Guid userId, Guid clusterId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.AccessRequests
            .Find(r => r.RequesterId == userId
                     && r.ClusterId == clusterId
                     && r.Status == AccessRequestStatus.Approved
                     && r.GrantExpiresAt > now
                     && r.RevokedAt == null)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Returns a MongoDB filter for active grants (approved, not expired, not revoked).
    /// Used by list queries.
    /// </summary>
    public static FilterDefinition<AccessRequest> ActiveGrantFilter()
    {
        var builder = Builders<AccessRequest>.Filter;
        var now = DateTimeOffset.UtcNow;
        return builder.Eq(r => r.Status, AccessRequestStatus.Approved)
             & builder.Gt(r => r.GrantExpiresAt, now)
             & builder.Eq(r => r.RevokedAt, null);
    }
}
