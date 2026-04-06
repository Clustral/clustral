using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Features.Clusters;
using Clustral.Sdk.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.ControlPlane.Api.Controllers.V1;

/// <summary>
/// Thin controller for cluster management. Delegates all business logic
/// to MediatR handlers in <see cref="Clustral.ControlPlane.Features.Clusters"/>.
/// </summary>
[ApiController]
[Route("api/v1/clusters")]
[Authorize]
public sealed class ClustersController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterClusterRestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterClusterRestRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new RegisterClusterCommand(request.Name, request.Description, request.AgentPublicKeyPem, request.Labels), ct);
        return result.Match<IActionResult>(
            value => CreatedAtAction(nameof(Get), new { id = value.ClusterId }, value),
            error => error.ToActionResult());
    }

    [HttpGet]
    [ProducesResponseType<ClusterListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? pageToken = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new ListClustersQuery(status, pageSize, pageToken), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClusterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetClusterQuery(id), ct);
        return result.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteClusterCommand(id), ct);
        return result.ToActionResult();
    }
}
