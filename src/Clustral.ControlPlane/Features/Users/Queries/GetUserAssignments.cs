using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Users.Queries;

public record GetUserAssignmentsQuery(Guid UserId) : IQuery<Result<RoleAssignmentListResponse>>;

public sealed class GetUserAssignmentsHandler(ClustralDb db)
    : IRequestHandler<GetUserAssignmentsQuery, Result<RoleAssignmentListResponse>>
{
    public async Task<Result<RoleAssignmentListResponse>> Handle(GetUserAssignmentsQuery request, CancellationToken ct)
    {
        var assignments = await db.RoleAssignments
            .Find(a => a.UserId == request.UserId)
            .ToListAsync(ct);

        var roleIds    = assignments.Select(a => a.RoleId).Distinct().ToList();
        var clusterIds = assignments.Select(a => a.ClusterId).Distinct().ToList();

        var roles    = (await db.Roles.Find(r => roleIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.Name);
        var clusters = (await db.Clusters.Find(c => clusterIds.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Name);

        var response = assignments.Select(a => new RoleAssignmentResponse(
            a.Id, a.UserId, a.RoleId,
            roles.GetValueOrDefault(a.RoleId, "unknown"),
            a.ClusterId,
            clusters.GetValueOrDefault(a.ClusterId, "unknown"),
            a.AssignedAt, a.AssignedBy)).ToList();

        return new RoleAssignmentListResponse(response);
    }
}
