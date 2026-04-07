using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Users;

public record AssignRoleCommand(Guid UserId, Guid RoleId, Guid ClusterId)
    : IRequest<Result<RoleAssignmentResponse>>;

public sealed class AssignRoleHandler(
    IUserRepository users,
    IRoleRepository roles,
    IClusterRepository clusters,
    IRoleAssignmentRepository assignments,
    ICurrentUserProvider currentUser,
    ILogger<AssignRoleHandler> logger)
    : IRequestHandler<AssignRoleCommand, Result<RoleAssignmentResponse>>
{
    public async Task<Result<RoleAssignmentResponse>> Handle(AssignRoleCommand request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);
        if (user is null) return ResultErrors.UserNotFound();

        var role = await roles.GetByIdAsync(request.RoleId, ct);
        if (role is null) return ResultErrors.RoleNotFound(request.RoleId.ToString());

        var cluster = await clusters.GetByIdAsync(request.ClusterId, ct);
        if (cluster is null) return ResultErrors.ClusterNotFound(request.ClusterId.ToString());

        var callerEmail = currentUser.Email ?? "unknown";

        var assignment = RoleAssignment.Create(
            request.UserId, request.RoleId, request.ClusterId, callerEmail);

        // Upsert: delete existing assignment for this user+cluster, then insert.
        await assignments.DeleteByUserAndClusterAsync(request.UserId, request.ClusterId, ct);
        await assignments.InsertAsync(assignment, ct);

        logger.LogInformation(
            "Assigned role {RoleName} to user {Email} on cluster {ClusterName}",
            role.Name, user.Email, cluster.Name);

        return new RoleAssignmentResponse(
            assignment.Id, request.UserId, role.Id, role.Name,
            cluster.Id, cluster.Name, assignment.AssignedAt, assignment.AssignedBy);
    }
}
