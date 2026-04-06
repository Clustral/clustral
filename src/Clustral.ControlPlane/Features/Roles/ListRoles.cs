using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Roles;

public record ListRolesQuery : IRequest<RoleListResponse>;

public sealed class ListRolesHandler(ClustralDb db)
    : IRequestHandler<ListRolesQuery, RoleListResponse>
{
    public async Task<RoleListResponse> Handle(ListRolesQuery request, CancellationToken ct)
    {
        var roles = await db.Roles
            .Find(FilterDefinition<Role>.Empty)
            .SortBy(r => r.Name)
            .ToListAsync(ct);

        var response = roles.Select(r => new RoleResponse(
            r.Id, r.Name, r.Description, r.KubernetesGroups, r.CreatedAt)).ToList();

        return new RoleListResponse(response);
    }
}
