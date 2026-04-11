using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Users.Commands;

public record RemoveAssignmentCommand(Guid UserId, Guid AssignmentId) : ICommand;

public sealed class RemoveAssignmentHandler(
    IRoleAssignmentRepository assignments,
    ICurrentUserProvider currentUser,
    IMediator mediator,
    ILogger<RemoveAssignmentHandler> logger)
    : IRequestHandler<RemoveAssignmentCommand, Result>
{
    public async Task<Result> Handle(RemoveAssignmentCommand request, CancellationToken ct)
    {
        var assignment = await assignments.GetByIdAsync(request.AssignmentId, ct);
        if (assignment is null)
            return ResultError.NotFound("ASSIGNMENT_NOT_FOUND", "Role assignment not found.");

        var deleted = await assignments.DeleteAsync(request.AssignmentId, ct);
        if (!deleted)
            return ResultError.NotFound("ASSIGNMENT_NOT_FOUND", "Role assignment not found.");

        await mediator.Publish(new RoleUnassigned(
            request.AssignmentId, assignment.UserId,
            assignment.RoleId, assignment.ClusterId, currentUser.Email), ct);

        logger.LogInformation("Removed role assignment {Id} by {Email}",
            request.AssignmentId, currentUser.Email);

        return Result.Success();
    }
}
