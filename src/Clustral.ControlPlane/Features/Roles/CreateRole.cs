using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Roles;

public record CreateRoleCommand(string Name, string Description, List<string>? KubernetesGroups)
    : IRequest<Result<RoleResponse>>;

public sealed class CreateRoleHandler(ClustralDb db, ILogger<CreateRoleHandler> logger)
    : IRequestHandler<CreateRoleCommand, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var exists = await db.Roles.Find(r => r.Name == request.Name).AnyAsync(ct);
        if (exists)
            return ResultErrors.DuplicateRoleName(request.Name);

        var role = Role.Create(request.Name, request.Description, request.KubernetesGroups);

        await db.Roles.InsertOneAsync(role, cancellationToken: ct);
        logger.LogInformation("Role {Name} created with id {Id}", role.Name, role.Id);

        return new RoleResponse(role.Id, role.Name, role.Description, role.KubernetesGroups, role.CreatedAt);
    }
}
