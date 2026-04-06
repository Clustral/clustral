using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Users;

public record RemoveAssignmentCommand(Guid UserId, Guid AssignmentId) : IRequest<Result>;

public sealed class RemoveAssignmentHandler(ClustralDb db, ILogger<RemoveAssignmentHandler> logger)
    : IRequestHandler<RemoveAssignmentCommand, Result>
{
    public async Task<Result> Handle(RemoveAssignmentCommand request, CancellationToken ct)
    {
        var result = await db.RoleAssignments.DeleteOneAsync(
            a => a.Id == request.AssignmentId && a.UserId == request.UserId, ct);

        if (result.DeletedCount == 0)
            return ResultError.NotFound("ASSIGNMENT_NOT_FOUND", "Role assignment not found.");

        logger.LogInformation("Removed role assignment {Id} for user {UserId}",
            request.AssignmentId, request.UserId);

        return Result.Success();
    }
}
