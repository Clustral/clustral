using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Auth;

public record RevokeByTokenCommand(string Token) : IRequest<Result<RevokeCredentialResponse>>;

public sealed class RevokeByTokenHandler(
    IAccessTokenRepository accessTokens,
    IMediator mediator,
    TokenHashingService tokens,
    ILogger<RevokeByTokenHandler> logger)
    : IRequestHandler<RevokeByTokenCommand, Result<RevokeCredentialResponse>>
{
    public async Task<Result<RevokeCredentialResponse>> Handle(
        RevokeByTokenCommand request, CancellationToken ct)
    {
        var hash = tokens.HashToken(request.Token);
        var credential = await accessTokens.GetByHashAsync(hash, ct);

        if (credential is null)
            return ResultErrors.CredentialNotFound();

        var now = DateTimeOffset.UtcNow;
        credential.RevokedAt = now;
        credential.RevokedReason = "logout";
        await accessTokens.ReplaceAsync(credential, ct);
        await mediator.Publish(new CredentialRevoked(credential.Id, "logout"), ct);

        logger.LogInformation("Credential {CredentialId} revoked via logout", credential.Id);

        return new RevokeCredentialResponse(Revoked: true, RevokedAt: now);
    }
}
