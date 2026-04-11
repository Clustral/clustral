using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Roles.Commands;

public record UpdateRoleCommand(Guid Id, string? Name, string? Description, List<string>? KubernetesGroups)
    : ICommand<Result<RoleResponse>>;

public sealed class UpdateRoleHandler(
    IRoleRepository roles,
    ICurrentUserProvider currentUser,
    IMediator mediator)
    : IRequestHandler<UpdateRoleCommand, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(request.Id, ct);
        if (role is null)
            return ResultErrors.RoleNotFound(request.Id.ToString());

        role.Update(request.Name, request.Description,
            request.KubernetesGroups, currentUser.Email);

        await roles.ReplaceAsync(role, ct);
        await mediator.DispatchDomainEventsAsync(role, ct);

        return new RoleResponse(role.Id, role.Name, role.Description, role.KubernetesGroups, role.CreatedAt);
    }
}
