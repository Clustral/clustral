using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Infrastructure.Auth;
using Clustral.Sdk.Results;
using MediatR;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Auth;

public record IssueKubeconfigCredentialCommand(Guid ClusterId, string? RequestedTtl)
    : IRequest<Result<IssueKubeconfigCredentialResponse>>;

public sealed class IssueKubeconfigCredentialHandler(
    ClustralDb db,
    IOptions<OidcOptions> oidcOptions,
    ICurrentUserProvider currentUser,
    TokenHashingService tokens,
    ILogger<IssueKubeconfigCredentialHandler> logger)
    : IRequestHandler<IssueKubeconfigCredentialCommand, Result<IssueKubeconfigCredentialResponse>>
{
    public async Task<Result<IssueKubeconfigCredentialResponse>> Handle(
        IssueKubeconfigCredentialCommand request, CancellationToken ct)
    {
        // 1. Verify cluster exists.
        var cluster = await db.Clusters
            .Find(c => c.Id == request.ClusterId)
            .FirstOrDefaultAsync(ct);

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

        // 3. Upsert user record.
        var subject = currentUser.Subject ?? string.Empty;
        var displayName = currentUser.DisplayName;
        var email = currentUser.Email;

        var existingUser = await db.Users
            .Find(u => u.KeycloakSubject == subject)
            .FirstOrDefaultAsync(ct);

        Guid userId;
        if (existingUser is null)
        {
            var newUser = new User
            {
                Id              = Guid.NewGuid(),
                KeycloakSubject = subject,
                DisplayName     = displayName,
                Email           = email,
            };
            await db.Users.InsertOneAsync(newUser, cancellationToken: ct);
            userId = newUser.Id;
        }
        else
        {
            userId = existingUser.Id;
            var update = Builders<User>.Update
                .Set(u => u.DisplayName, displayName)
                .Set(u => u.Email, email)
                .Set(u => u.LastSeenAt, DateTimeOffset.UtcNow);
            await db.Users.UpdateOneAsync(u => u.Id == existingUser.Id, update, cancellationToken: ct);
        }

        // 4. Generate token.
        var rawToken = tokens.GenerateToken();
        var tokenHash = tokens.HashToken(rawToken);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + ttl;

        // Cap credential TTL to JIT grant expiry if no static assignment.
        var staticAssignment = await db.RoleAssignments
            .Find(a => a.UserId == userId && a.ClusterId == cluster.Id)
            .FirstOrDefaultAsync(ct);

        if (staticAssignment is null)
        {
            var activeGrant = await db.AccessRequests
                .Find(r => r.RequesterId == userId
                         && r.ClusterId == cluster.Id
                         && r.Status == AccessRequestStatus.Approved
                         && r.GrantExpiresAt > now
                         && r.RevokedAt == null)
                .FirstOrDefaultAsync(ct);

            if (activeGrant?.GrantExpiresAt is not null && activeGrant.GrantExpiresAt.Value < expiresAt)
                expiresAt = activeGrant.GrantExpiresAt.Value;
        }

        var credential = new AccessToken
        {
            Id        = Guid.NewGuid(),
            Kind      = CredentialKind.UserKubeconfig,
            TokenHash = tokenHash,
            ClusterId = cluster.Id,
            UserId    = userId,
            IssuedAt  = now,
            ExpiresAt = expiresAt,
        };
        await db.AccessTokens.InsertOneAsync(credential, cancellationToken: ct);

        logger.LogInformation(
            "Issued kubeconfig credential {CredentialId} for user {Subject} on cluster {ClusterName}",
            credential.Id, subject, cluster.Name);

        return new IssueKubeconfigCredentialResponse(
            credential.Id, rawToken, credential.IssuedAt, credential.ExpiresAt, subject, displayName);
    }
}
