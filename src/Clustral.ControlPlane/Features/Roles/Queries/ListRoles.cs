using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Roles.Queries;

public record ListRolesQuery : IQuery<Result<RoleListResponse>>;

public sealed class ListRolesHandler(IRoleRepository roles)
    : IRequestHandler<ListRolesQuery, Result<RoleListResponse>>
{
    public async Task<Result<RoleListResponse>> Handle(ListRolesQuery request, CancellationToken ct)
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
