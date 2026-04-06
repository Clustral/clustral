using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Users;

public record AssignRoleCommand(Guid UserId, Guid RoleId, Guid ClusterId)
    : IRequest<Result<RoleAssignmentResponse>>;

public sealed class AssignRoleHandler(
    ClustralDb db,
    ICurrentUserProvider currentUser,
    ILogger<AssignRoleHandler> logger)
    : IRequestHandler<AssignRoleCommand, Result<RoleAssignmentResponse>>
{
    public async Task<Result<RoleAssignmentResponse>> Handle(AssignRoleCommand request, CancellationToken ct)
    {
        var user = await db.Users.Find(u => u.Id == request.UserId).FirstOrDefaultAsync(ct);
        if (user is null) return ResultErrors.UserNotFound();

        var role = await db.Roles.Find(r => r.Id == request.RoleId).FirstOrDefaultAsync(ct);
        if (role is null) return ResultErrors.RoleNotFound(request.RoleId.ToString());

        var cluster = await db.Clusters.Find(c => c.Id == request.ClusterId).FirstOrDefaultAsync(ct);
        if (cluster is null) return ResultErrors.ClusterNotFound(request.ClusterId.ToString());

        var callerEmail = currentUser.Email ?? "unknown";

        var assignment = new RoleAssignment
        {
            Id         = Guid.NewGuid(),
            UserId     = request.UserId,
            RoleId     = request.RoleId,
            ClusterId  = request.ClusterId,
            AssignedBy = callerEmail,
        };

        // Upsert: delete existing assignment for this user+cluster, then insert.
        await db.RoleAssignments.DeleteManyAsync(
            a => a.UserId == request.UserId && a.ClusterId == request.ClusterId, ct);
        await db.RoleAssignments.InsertOneAsync(assignment, cancellationToken: ct);

        logger.LogInformation(
            "Assigned role {RoleName} to user {Email} on cluster {ClusterName}",
            role.Name, user.Email, cluster.Name);

        return new RoleAssignmentResponse(
            assignment.Id, request.UserId, role.Id, role.Name,
            cluster.Id, cluster.Name, assignment.AssignedAt, assignment.AssignedBy);
    }
}
