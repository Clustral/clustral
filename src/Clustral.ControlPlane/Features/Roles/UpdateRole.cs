using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Roles;

public record UpdateRoleCommand(Guid Id, string? Name, string? Description, List<string>? KubernetesGroups)
    : IRequest<Result<RoleResponse>>;

public sealed class UpdateRoleHandler(ClustralDb db)
    : IRequestHandler<UpdateRoleCommand, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var update = Builders<Role>.Update.Combine();
        if (request.Name is not null)
            update = update.Set(r => r.Name, request.Name);
        if (request.Description is not null)
            update = update.Set(r => r.Description, request.Description);
        if (request.KubernetesGroups is not null)
            update = update.Set(r => r.KubernetesGroups, request.KubernetesGroups);

        var result = await db.Roles.UpdateOneAsync(r => r.Id == request.Id, update, cancellationToken: ct);
        if (result.MatchedCount == 0)
            return ResultErrors.RoleNotFound(request.Id.ToString());

        var role = await db.Roles.Find(r => r.Id == request.Id).FirstOrDefaultAsync(ct);
        return new RoleResponse(role!.Id, role.Name, role.Description, role.KubernetesGroups, role.CreatedAt);
    }
}
