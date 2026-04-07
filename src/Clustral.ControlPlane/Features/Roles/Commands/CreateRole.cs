using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Roles.Commands;

public record CreateRoleCommand(string Name, string Description, List<string>? KubernetesGroups)
    : ICommand<Result<RoleResponse>>;

public sealed class CreateRoleHandler(IRoleRepository roles, IMediator mediator, ILogger<CreateRoleHandler> logger)
    : IRequestHandler<CreateRoleCommand, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var exists = await roles.ExistsByNameAsync(request.Name, ct);
        if (exists)
            return ResultErrors.DuplicateRoleName(request.Name);

        var role = Role.Create(request.Name, request.Description, request.KubernetesGroups);

        await roles.InsertAsync(role, ct);
        await mediator.DispatchDomainEventsAsync(role, ct);
        logger.LogInformation("Role {Name} created with id {Id}", role.Name, role.Id);

        return new RoleResponse(role.Id, role.Name, role.Description, role.KubernetesGroups, role.CreatedAt);
    }
}
