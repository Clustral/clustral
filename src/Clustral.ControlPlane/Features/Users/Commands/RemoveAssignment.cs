using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Users.Commands;

public record RemoveAssignmentCommand(Guid UserId, Guid AssignmentId) : ICommand;

public sealed class RemoveAssignmentHandler(
    IRoleAssignmentRepository assignments,
    IMediator mediator,
    ILogger<RemoveAssignmentHandler> logger)
    : IRequestHandler<RemoveAssignmentCommand, Result>
{
    public async Task<Result> Handle(RemoveAssignmentCommand request, CancellationToken ct)
    {
        var deleted = await assignments.DeleteAsync(request.AssignmentId, ct);

        if (!deleted)
            return ResultError.NotFound("ASSIGNMENT_NOT_FOUND", "Role assignment not found.");

        await mediator.Publish(new RoleUnassigned(request.AssignmentId), ct);

        logger.LogInformation("Removed role assignment {Id} for user {UserId}",
            request.AssignmentId, request.UserId);

        return Result.Success();
    }
}
