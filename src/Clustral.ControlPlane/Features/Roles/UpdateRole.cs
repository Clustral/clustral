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
        var role = await db.Roles.Find(r => r.Id == request.Id).FirstOrDefaultAsync(ct);
        if (role is null)
            return ResultErrors.RoleNotFound(request.Id.ToString());

        role.Update(request.Name, request.Description, request.KubernetesGroups);

        await db.Roles.ReplaceOneAsync(r => r.Id == role.Id, role, cancellationToken: ct);

        return new RoleResponse(role.Id, role.Name, role.Description, role.KubernetesGroups, role.CreatedAt);
    }
}
