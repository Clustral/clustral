using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Roles;

public record DeleteRoleCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteRoleHandler(ClustralDb db, ILogger<DeleteRoleHandler> logger)
    : IRequestHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var deleteResult = await db.Roles.DeleteOneAsync(r => r.Id == request.Id, ct);
        if (deleteResult.DeletedCount == 0)
            return ResultErrors.RoleNotFound(request.Id.ToString());

        // Cascade: remove all assignments for this role.
        await db.RoleAssignments.DeleteManyAsync(a => a.RoleId == request.Id, ct);
        logger.LogInformation("Role {RoleId} deleted", request.Id);

        return Result.Success();
    }
}
