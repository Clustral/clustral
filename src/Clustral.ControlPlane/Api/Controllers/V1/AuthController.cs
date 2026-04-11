using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Features.Auth.Commands;
using Clustral.Sdk.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.ControlPlane.Api.Controllers.V1;

/// <summary>
/// Thin controller for credential issuance and revocation.
/// Delegates to MediatR handlers in <see cref="Clustral.ControlPlane.Features.Auth"/>.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Authorize]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    [HttpPost("kubeconfig-credential")]
    [ProducesResponseType<IssueKubeconfigCredentialResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IssueKubeconfigCredential(
        [FromBody] IssueKubeconfigCredentialRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new IssueKubeconfigCredentialCommand(request.ClusterId, request.RequestedTtl), ct);
        return result.Match<IActionResult>(
            value => CreatedAtAction(nameof(IssueKubeconfigCredential), value),
            error => error.ToActionResult());
    }

    [HttpDelete("credentials/{id:guid}")]
    [ProducesResponseType<RevokeCredentialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeCredential(
        Guid id, [FromBody] RevokeCredentialRequest? request, CancellationToken ct)
    {
        var result = await mediator.Send(new RevokeCredentialCommand(id, request?.Reason), ct);
        return result.ToActionResult();
    }

    [HttpPost("revoke-by-token")]
    [ProducesResponseType<RevokeCredentialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeByToken(
        [FromBody] RevokeByTokenRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new RevokeByTokenCommand(request.Token), ct);
        return result.ToActionResult();
    }

    /// <summary>
    /// Token hashing — delegates to <c>TokenHashingService</c>.
    /// </summary>
    internal static string HashToken(string rawToken) =>
        new Features.Shared.TokenHashingService().HashToken(rawToken);
}
