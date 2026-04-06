using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Auth;

public record RevokeByTokenCommand(string Token) : IRequest<Result<RevokeCredentialResponse>>;

public sealed class RevokeByTokenHandler(
    ClustralDb db,
    TokenHashingService tokens,
    ILogger<RevokeByTokenHandler> logger)
    : IRequestHandler<RevokeByTokenCommand, Result<RevokeCredentialResponse>>
{
    public async Task<Result<RevokeCredentialResponse>> Handle(
        RevokeByTokenCommand request, CancellationToken ct)
    {
        var hash = tokens.HashToken(request.Token);
        var credential = await db.AccessTokens
            .Find(t => t.TokenHash == hash && t.Kind == CredentialKind.UserKubeconfig)
            .FirstOrDefaultAsync(ct);

        if (credential is null)
            return ResultErrors.CredentialNotFound();

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessToken>.Update
            .Set(t => t.RevokedAt, now)
            .Set(t => t.RevokedReason, "logout");
        await db.AccessTokens.UpdateOneAsync(t => t.Id == credential.Id, update, cancellationToken: ct);

        logger.LogInformation("Credential {CredentialId} revoked via logout", credential.Id);

        return new RevokeCredentialResponse(Revoked: true, RevokedAt: now);
    }
}
