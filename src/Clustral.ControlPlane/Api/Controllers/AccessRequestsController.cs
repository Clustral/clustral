using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Features.AccessRequests;
using Clustral.Sdk.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.ControlPlane.Api.Controllers;

/// <summary>
/// Thin controller for JIT access requests. Delegates to MediatR handlers
/// in <see cref="Clustral.ControlPlane.Features.AccessRequests"/>.
/// </summary>
[ApiController]
[Route("api/v1/access-requests")]
[Authorize]
public sealed class AccessRequestsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccessRequestRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateAccessRequestCommand(
            request.RoleId, request.ClusterId, request.Reason,
            request.RequestedDuration, request.SuggestedReviewerEmails), ct);
        return result.Match<IActionResult>(
            value => CreatedAtAction(nameof(Get), new { id = value.Id }, value),
            error => error.ToActionResult());
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] bool mine, [FromQuery] bool active,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ListAccessRequestsQuery(status, mine, active), ct);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAccessRequestQuery(id), ct);
        return result.ToActionResult();
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] ApproveAccessRequestRequest? body, CancellationToken ct)
    {
        var result = await mediator.Send(
            new ApproveAccessRequestCommand(id, body?.DurationOverride), ct);
        return result.ToActionResult();
    }

    [HttpPost("{id:guid}/deny")]
    public async Task<IActionResult> Deny(
        Guid id, [FromBody] DenyAccessRequestRequest body, CancellationToken ct)
    {
        var result = await mediator.Send(new DenyAccessRequestCommand(id, body.Reason), ct);
        return result.ToActionResult();
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(
        Guid id, [FromBody] RevokeAccessRequestRequest? body, CancellationToken ct)
    {
        var result = await mediator.Send(new RevokeAccessRequestCommand(id, body?.Reason), ct);
        return result.ToActionResult();
    }
}
