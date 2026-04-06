using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Features.Roles;
using Clustral.Sdk.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.ControlPlane.Api.Controllers.V1;

[ApiController]
[Route("api/v1/roles")]
[Authorize]
public sealed class RolesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new ListRolesQuery(), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateRoleCommand(request.Name, request.Description, request.KubernetesGroups), ct);
        return result.ToCreatedResult(nameof(List));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateRoleCommand(id, request.Name, request.Description, request.KubernetesGroups), ct);
        return result.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteRoleCommand(id), ct);
        return result.ToActionResult();
    }
}
