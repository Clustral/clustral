using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Auth;

public record RevokeCredentialCommand(Guid CredentialId, string? Reason)
    : IRequest<Result<RevokeCredentialResponse>>;

public sealed class RevokeCredentialHandler(
    ClustralDb db,
    ICurrentUserProvider currentUser,
    ILogger<RevokeCredentialHandler> logger)
    : IRequestHandler<RevokeCredentialCommand, Result<RevokeCredentialResponse>>
{
    public async Task<Result<RevokeCredentialResponse>> Handle(
        RevokeCredentialCommand request, CancellationToken ct)
    {
        var credential = await db.AccessTokens
            .Find(t => t.Id == request.CredentialId)
            .FirstOrDefaultAsync(ct);

        if (credential is null)
            return ResultErrors.CredentialNotFound();

        // Ownership check.
        if (credential.UserId.HasValue)
        {
            var owner = await db.Users
                .Find(u => u.Id == credential.UserId.Value)
                .FirstOrDefaultAsync(ct);
            if (owner?.KeycloakSubject != currentUser.Subject)
                return ResultErrors.CredentialOwnerMismatch();
        }

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessToken>.Update
            .Set(t => t.RevokedAt, now)
            .Set(t => t.RevokedReason, request.Reason);
        await db.AccessTokens.UpdateOneAsync(t => t.Id == request.CredentialId, update, cancellationToken: ct);

        logger.LogInformation("Credential {CredentialId} revoked by {Subject}",
            request.CredentialId, currentUser.Subject);

        return new RevokeCredentialResponse(Revoked: true, RevokedAt: now);
    }
}
