using Clustral.ControlPlane.Domain.Repositories;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Users;

public record RemoveAssignmentCommand(Guid UserId, Guid AssignmentId) : IRequest<Result>;

public sealed class RemoveAssignmentHandler(
    IRoleAssignmentRepository assignments,
    ILogger<RemoveAssignmentHandler> logger)
    : IRequestHandler<RemoveAssignmentCommand, Result>
{
    public async Task<Result> Handle(RemoveAssignmentCommand request, CancellationToken ct)
    {
        var deleted = await assignments.DeleteAsync(request.AssignmentId, ct);

        if (!deleted)
            return ResultError.NotFound("ASSIGNMENT_NOT_FOUND", "Role assignment not found.");

        logger.LogInformation("Removed role assignment {Id} for user {UserId}",
            request.AssignmentId, request.UserId);

        return Result.Success();
    }
}
