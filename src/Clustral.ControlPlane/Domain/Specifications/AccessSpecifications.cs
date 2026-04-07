using Clustral.ControlPlane.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Domain.Specifications;

/// <summary>
/// Reusable query predicates for access-related lookups.
/// Eliminates duplication across CreateAccessRequest, IssueKubeconfigCredential,
/// and ListAccessRequests handlers.
/// </summary>
public sealed class AccessSpecifications(
    IRoleAssignmentRepository assignments,
    IAccessRequestRepository accessRequests)
{
    /// <summary>
    /// Returns true if the user has a static role assignment for the given cluster.
    /// </summary>
    public async Task<bool> HasStaticAssignmentAsync(
        Guid userId, Guid clusterId, CancellationToken ct = default)
    {
        var userAssignments = await assignments.GetByUserIdAsync(userId, ct);
        return userAssignments.Any(a => a.ClusterId == clusterId);
    }

    /// <summary>
    /// Returns the pending access request for the user+cluster, or null.
    /// </summary>
    public async Task<AccessRequest?> GetPendingRequestAsync(
        Guid userId, Guid clusterId, CancellationToken ct = default)
    {
        var builder = Builders<AccessRequest>.Filter;
        var filter = builder.Eq(r => r.RequesterId, userId)
                   & builder.Eq(r => r.ClusterId, clusterId)
                   & builder.Eq(r => r.Status, AccessRequestStatus.Pending);

        var results = await accessRequests.FindAsync(filter, 1, ct);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Returns the active approved grant (not expired, not revoked) for the
    /// user+cluster, or null.
    /// </summary>
    public async Task<AccessRequest?> GetActiveGrantAsync(
        Guid userId, Guid clusterId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var builder = Builders<AccessRequest>.Filter;
        var filter = builder.Eq(r => r.RequesterId, userId)
                   & builder.Eq(r => r.ClusterId, clusterId)
                   & builder.Eq(r => r.Status, AccessRequestStatus.Approved)
                   & builder.Gt(r => r.GrantExpiresAt, now)
                   & builder.Eq(r => r.RevokedAt, null);

        var results = await accessRequests.FindAsync(filter, 1, ct);
        return results.FirstOrDefault();
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
