using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Auth.Commands;

public record RevokeCredentialCommand(Guid CredentialId, string? Reason)
    : ICommand<Result<RevokeCredentialResponse>>;

public sealed class RevokeCredentialHandler(
    ClustralDb db,
    IUserRepository users,
    IMediator mediator,
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

        if (credential.UserId.HasValue)
        {
            var owner = await users.GetByIdAsync(credential.UserId.Value, ct);
            if (owner?.KeycloakSubject != currentUser.Subject)
                return ResultErrors.CredentialOwnerMismatch();
        }

        var now = DateTimeOffset.UtcNow;
        credential.RevokedAt = now;
        credential.RevokedReason = request.Reason;
        await db.AccessTokens.ReplaceOneAsync(t => t.Id == credential.Id, credential, cancellationToken: ct);
        await mediator.Publish(new CredentialRevoked(request.CredentialId, request.Reason, currentUser.Email), ct);

        logger.LogInformation("Credential {CredentialId} revoked by {Email}",
            request.CredentialId, currentUser.Email);

        return new RevokeCredentialResponse(Revoked: true, RevokedAt: now);
    }
}
