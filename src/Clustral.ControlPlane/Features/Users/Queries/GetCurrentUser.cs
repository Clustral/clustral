using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Users.Queries;

public record GetCurrentUserQuery : IQuery<Result<UserProfileResponse>>;

public sealed class GetCurrentUserHandler(
    ICurrentUserProvider currentUser,
    ClustralDb db)
    : IRequestHandler<GetCurrentUserQuery, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currentUser.Subject))
            return ResultErrors.UserUnauthorized();

        var user = await currentUser.GetCurrentUserAsync(ct);
        if (user is null)
            return ResultErrors.UserNotFound();

        var assignments = await db.RoleAssignments
            .Find(a => a.UserId == user.Id)
            .ToListAsync(ct);

        var roleIds    = assignments.Select(a => a.RoleId).Distinct().ToList();
        var clusterIds = assignments.Select(a => a.ClusterId).Distinct().ToList();

        var roles    = (await db.Roles.Find(r => roleIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id);
        var clusters = (await db.Clusters.Find(c => clusterIds.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);

        var assignmentResponses = assignments.Select(a => new RoleAssignmentResponse(
            a.Id, a.UserId, a.RoleId,
            roles.GetValueOrDefault(a.RoleId)?.Name ?? "unknown",
            a.ClusterId,
            clusters.GetValueOrDefault(a.ClusterId)?.Name ?? "unknown",
            a.AssignedAt, a.AssignedBy)).ToList();

        // Active JIT grants.
        var now = DateTimeOffset.UtcNow;
        var activeRequests = await db.AccessRequests
            .Find(r => r.RequesterId == user.Id
                     && r.Status == AccessRequestStatus.Approved
                     && r.GrantExpiresAt > now
                     && r.RevokedAt == null)
            .ToListAsync(ct);

        var grantClusterIds = activeRequests.Select(r => r.ClusterId).Distinct().ToList();
        var grantRoleIds    = activeRequests.Select(r => r.RoleId).Distinct().ToList();
        var grantClusters   = (await db.Clusters.Find(c => grantClusterIds.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
        var grantRoles      = (await db.Roles.Find(r => grantRoleIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id);

        var activeGrants = activeRequests.Select(r => new ActiveGrantResponse(
            r.Id,
            grantRoles.GetValueOrDefault(r.RoleId)?.Name ?? "unknown",
            r.ClusterId,
            grantClusters.GetValueOrDefault(r.ClusterId)?.Name ?? "unknown",
            r.GrantExpiresAt!.Value)).ToList();

        return new UserProfileResponse(
            user.Id,
            user.Email ?? user.KeycloakSubject,
            user.DisplayName,
            user.CreatedAt,
            user.LastSeenAt,
            assignmentResponses,
            activeGrants);
    }
}
