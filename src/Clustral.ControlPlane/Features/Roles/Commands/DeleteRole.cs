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
    IMediator mediator,
    ILogger<DeleteRoleHandler> logger)
    : IRequestHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var deleted = await roles.DeleteAsync(request.Id, ct);
        if (!deleted)
            return ResultErrors.RoleNotFound(request.Id.ToString());

        // Cascade: remove all assignments for this role.
        await assignments.DeleteByRoleIdAsync(request.Id, ct);
        await mediator.Publish(new RoleDeleted(request.Id), ct);

        logger.LogInformation("Role {RoleId} deleted", request.Id);

        return Result.Success();
    }
}
