using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Features.Users.Commands;
using Clustral.ControlPlane.Features.Users.Queries;
using Clustral.Sdk.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.ControlPlane.Api.Controllers.V1;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(IMediator mediator) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await mediator.Send(new GetCurrentUserQuery(), ct);
        return result.ToActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await mediator.Send(new ListUsersQuery(), ct);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}/assignments")]
    public async Task<IActionResult> GetAssignments(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetUserAssignmentsQuery(id), ct);
        return result.ToActionResult();
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> AssignRole(
        Guid id, [FromBody] AssignRoleRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new AssignRoleCommand(id, request.RoleId, request.ClusterId), ct);
        return result.Match<IActionResult>(
            value => CreatedAtAction(nameof(GetAssignments), new { id }, value),
            error => error.ToActionResult());
    }

    [HttpDelete("{userId:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> RemoveAssignment(Guid userId, Guid assignmentId, CancellationToken ct)
    {
        var result = await mediator.Send(new RemoveAssignmentCommand(userId, assignmentId), ct);
        return result.ToActionResult();
    }
}
