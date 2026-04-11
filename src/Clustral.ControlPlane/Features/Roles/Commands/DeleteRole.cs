using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Roles.Commands;

public record DeleteRoleCommand(Guid Id) : ICommand;

public sealed class DeleteRoleHandler(
    IRoleRepository roles,
    IRoleAssignmentRepository assignments,
    ICurrentUserProvider currentUser,
    IMediator mediator,
    ILogger<DeleteRoleHandler> logger)
    : IRequestHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(request.Id, ct);
        if (role is null)
            return ResultErrors.RoleNotFound(request.Id.ToString());

        var deleted = await roles.DeleteAsync(request.Id, ct);
        if (!deleted)
            return ResultErrors.RoleNotFound(request.Id.ToString());

        await assignments.DeleteByRoleIdAsync(request.Id, ct);
        await mediator.Publish(new RoleDeleted(request.Id, role.Name, currentUser.Email), ct);

        logger.LogInformation("Role {RoleId} ({Name}) deleted by {Email}",
            request.Id, role.Name, currentUser.Email);

        return Result.Success();
    }
}
