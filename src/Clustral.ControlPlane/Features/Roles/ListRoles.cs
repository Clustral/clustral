using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Repositories;
using MediatR;

namespace Clustral.ControlPlane.Features.Roles;

public record ListRolesQuery : IRequest<RoleListResponse>;

public sealed class ListRolesHandler(IRoleRepository roles)
    : IRequestHandler<ListRolesQuery, RoleListResponse>
{
    public async Task<RoleListResponse> Handle(ListRolesQuery request, CancellationToken ct)
    {
        var allRoles = await roles.ListAsync(ct);

        var response = allRoles
            .OrderBy(r => r.Name)
            .Select(r => new RoleResponse(
                r.Id, r.Name, r.Description, r.KubernetesGroups, r.CreatedAt))
            .ToList();

        return new RoleListResponse(response);
    }
}
