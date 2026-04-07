using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Domain.Services;
using Clustral.ControlPlane.Domain.Specifications;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure.Auth;
using Clustral.Sdk.Results;
using MediatR;
using Microsoft.Extensions.Options;

namespace Clustral.ControlPlane.Features.Auth.Commands;

public record IssueKubeconfigCredentialCommand(Guid ClusterId, string? RequestedTtl)
    : ICommand<Result<IssueKubeconfigCredentialResponse>>;

public sealed class IssueKubeconfigCredentialHandler(
    IClusterRepository clusters,
    IAccessTokenRepository accessTokens,
    IOptions<OidcOptions> oidcOptions,
    ICurrentUserProvider currentUser,
    IMediator mediator,
    UserSyncService userSync,
    AccessSpecifications specs,
    TokenHashingService tokens,
    ILogger<IssueKubeconfigCredentialHandler> logger)
    : IRequestHandler<IssueKubeconfigCredentialCommand, Result<IssueKubeconfigCredentialResponse>>
{
    public async Task<Result<IssueKubeconfigCredentialResponse>> Handle(
        IssueKubeconfigCredentialCommand request, CancellationToken ct)
    {
        // 1. Verify cluster exists.
        var cluster = await clusters.GetByIdAsync(request.ClusterId, ct);

        if (cluster is null)
            return ResultErrors.ClusterNotFound(request.ClusterId.ToString());

        // 2. Determine TTL.
        var opts = oidcOptions.Value;
        var ttl = opts.DefaultKubeconfigCredentialTtl;

        if (!string.IsNullOrEmpty(request.RequestedTtl) &&
            TimeSpan.TryParse(request.RequestedTtl, out var requestedTtl))
        {
            ttl = requestedTtl < opts.MaxKubeconfigCredentialTtl
                ? requestedTtl
                : opts.MaxKubeconfigCredentialTtl;
        }

        // 3. Upsert user via domain service.
        var subject = currentUser.Subject ?? string.Empty;
        var user = await userSync.SyncFromOidcClaimsAsync(
            subject, currentUser.Email, currentUser.DisplayName, ct);

        // 4. Generate token.
        var rawToken = tokens.GenerateToken();
        var tokenHash = tokens.HashToken(rawToken);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + ttl;

        // 5. Cap credential TTL to JIT grant expiry if no static assignment.
        if (!await specs.HasStaticAssignmentAsync(user.Id, cluster.Id, ct))
        {
            var activeGrant = await specs.GetActiveGrantAsync(user.Id, cluster.Id, ct);
            if (activeGrant?.GrantExpiresAt is not null && activeGrant.GrantExpiresAt.Value < expiresAt)
                expiresAt = activeGrant.GrantExpiresAt.Value;
        }

        var credential = new AccessToken
        {
            Id        = Guid.NewGuid(),
            Kind      = CredentialKind.UserKubeconfig,
            TokenHash = tokenHash,
            ClusterId = cluster.Id,
            UserId    = user.Id,
            IssuedAt  = now,
            ExpiresAt = expiresAt,
        };
        await accessTokens.InsertAsync(credential, ct);
        await mediator.Publish(new CredentialIssued(credential.Id, user.Id, cluster.Id, expiresAt), ct);

        logger.LogInformation(
            "Issued kubeconfig credential {CredentialId} for user {Subject} on cluster {ClusterName}",
            credential.Id, subject, cluster.Name);

        return new IssueKubeconfigCredentialResponse(
            credential.Id, rawToken, credential.IssuedAt, credential.ExpiresAt,
            subject, currentUser.DisplayName);
    }
}
