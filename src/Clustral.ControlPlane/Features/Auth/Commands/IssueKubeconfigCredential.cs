using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.Sdk.Auth;
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
    KubeconfigJwtService kubeconfigJwtService,
    ILogger<IssueKubeconfigCredentialHandler> logger)
    : IRequestHandler<IssueKubeconfigCredentialCommand, Result<IssueKubeconfigCredentialResponse>>
{
    public async Task<Result<IssueKubeconfigCredentialResponse>> Handle(
        IssueKubeconfigCredentialCommand request, CancellationToken ct)
    {
        // 1. Verify cluster exists.
        var cluster = await clusters.GetByIdAsync(request.ClusterId, ct);

        if (cluster is null)
        {
            await mediator.Publish(new CredentialIssueFailed(
                request.ClusterId, "Cluster not found", currentUser.Email), ct);
            return ResultErrors.ClusterNotFound(request.ClusterId.ToString());
        }

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

        // 4. Calculate expiry.
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + ttl;

        // 5. Cap credential TTL to JIT grant expiry if no static assignment.
        if (!await specs.HasStaticAssignmentAsync(user.Id, cluster.Id, ct))
        {
            var activeGrant = await specs.GetActiveGrantAsync(user.Id, cluster.Id, ct);
            if (activeGrant?.GrantExpiresAt is not null && activeGrant.GrantExpiresAt.Value < expiresAt)
                expiresAt = activeGrant.GrantExpiresAt.Value;
        }

        // 6. Issue kubeconfig JWT (ES256) instead of random token.
        var credentialId = Guid.NewGuid();
        var kubeconfigJwt = kubeconfigJwtService.Issue(credentialId, user.Id, cluster.Id, expiresAt);
        var tokenHash = tokens.HashToken(kubeconfigJwt);

        var credential = new AccessToken
        {
            Id        = credentialId,
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
            "Issued kubeconfig JWT credential {CredentialId} for user {Subject} on cluster {ClusterName}",
            credential.Id, subject, cluster.Name);

        return new IssueKubeconfigCredentialResponse(
            credential.Id, kubeconfigJwt, credential.IssuedAt, credential.ExpiresAt,
            subject, currentUser.DisplayName);
    }
}
