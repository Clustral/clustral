using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.AccessRequests;

/// <summary>
/// Enriches an <see cref="AccessRequest"/> domain entity with resolved
/// user/role/cluster names for the API response.
/// </summary>
public sealed class AccessRequestEnricher(ClustralDb db)
{
    public async Task<AccessRequestResponse> EnrichAsync(AccessRequest r, CancellationToken ct)
    {
        var requester = await db.Users.Find(u => u.Id == r.RequesterId).FirstOrDefaultAsync(ct);
        var role      = await db.Roles.Find(rl => rl.Id == r.RoleId).FirstOrDefaultAsync(ct);
        var cluster   = await db.Clusters.Find(c => c.Id == r.ClusterId).FirstOrDefaultAsync(ct);

        string? reviewerEmail = null;
        if (r.ReviewerId.HasValue)
        {
            var reviewer = await db.Users.Find(u => u.Id == r.ReviewerId.Value).FirstOrDefaultAsync(ct);
            reviewerEmail = reviewer?.Email ?? reviewer?.KeycloakSubject;
        }

        var reviewerInfos = new List<ReviewerInfo>();
        foreach (var reviewerId in r.SuggestedReviewers)
        {
            var u = await db.Users.Find(u => u.Id == reviewerId).FirstOrDefaultAsync(ct);
            if (u is not null)
                reviewerInfos.Add(new ReviewerInfo(u.Id, u.Email ?? u.KeycloakSubject, u.DisplayName));
        }

        string? revokedByEmail = null;
        if (r.RevokedBy.HasValue)
        {
            var revoker = await db.Users.Find(u => u.Id == r.RevokedBy.Value).FirstOrDefaultAsync(ct);
            revokedByEmail = revoker?.Email ?? revoker?.KeycloakSubject;
        }

        return new AccessRequestResponse(
            r.Id, r.RequesterId,
            requester?.Email ?? requester?.KeycloakSubject ?? "unknown",
            requester?.DisplayName,
            r.RoleId, role?.Name ?? "unknown",
            r.ClusterId, cluster?.Name ?? "unknown",
            r.Status.ToString(), r.Reason, r.RequestedDuration.ToString(),
            r.CreatedAt, r.RequestExpiresAt, reviewerInfos,
            r.ReviewerId, reviewerEmail, r.ReviewedAt, r.DenialReason,
            r.GrantExpiresAt, r.RevokedAt, revokedByEmail, r.RevokedReason);
    }
}
