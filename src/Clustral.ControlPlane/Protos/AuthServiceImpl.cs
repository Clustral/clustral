using System.Security.Cryptography;
using System.Text;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Infrastructure.Auth;
using Clustral.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using DomainAccessToken    = Clustral.ControlPlane.Domain.AccessToken;
using DomainCredentialKind = Clustral.ControlPlane.Domain.CredentialKind;

namespace Clustral.ControlPlane.Protos;

/// <summary>
/// gRPC server implementation of <c>AuthService</c>.
/// Issues and validates Clustral bearer tokens for both users (kubeconfig
/// credentials) and agents.
/// </summary>
public sealed class AuthServiceImpl(
    ClustralDb db,
    IOptions<OidcOptions> oidcOpts,
    ILogger<AuthServiceImpl> logger)
    : AuthService.AuthServiceBase
{
    // ── IssueKubeconfigCredential ─────────────────────────────────────────────

    public override async Task<IssueKubeconfigCredentialResponse> IssueKubeconfigCredential(
        IssueKubeconfigCredentialRequest request,
        ServerCallContext context)
    {
        // TODO: Validate the OIDC access token in request.OidcAccessToken
        //       using the JWKS endpoint configured in OidcOptions.
        //       For now we accept any non-empty token (dev scaffolding only).
        if (string.IsNullOrWhiteSpace(request.OidcAccessToken))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "oidc_access_token is required"));

        if (!Guid.TryParse(request.ClusterId, out var clusterId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "cluster_id must be a valid UUID"));

        var cluster = await db.Clusters
            .Find(c => c.Id == clusterId)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (cluster is null)
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Cluster {request.ClusterId} not found"));

        var opts = oidcOpts.Value;
        var ttl  = opts.DefaultKubeconfigCredentialTtl;

        if (request.RequestedTtl is { } requestedTtl)
        {
            var requested = requestedTtl.ToTimeSpan();
            ttl = requested < opts.MaxKubeconfigCredentialTtl
                ? requested
                : opts.MaxKubeconfigCredentialTtl;
        }

        var rawToken = GenerateToken();
        var now      = DateTimeOffset.UtcNow;

        var credential = new DomainAccessToken
        {
            Id        = Guid.NewGuid(),
            Kind      = DomainCredentialKind.UserKubeconfig,
            TokenHash = HashToken(rawToken),
            ClusterId = clusterId,
            IssuedAt  = now,
            ExpiresAt = now + ttl,
        };
        await db.AccessTokens.InsertOneAsync(credential, cancellationToken: context.CancellationToken);

        logger.LogInformation(
            "Kubeconfig credential {Id} issued for cluster {ClusterId}, expires {ExpiresAt}",
            credential.Id, clusterId, credential.ExpiresAt);

        return new IssueKubeconfigCredentialResponse
        {
            CredentialId = credential.Id.ToString(),
            Token        = rawToken,
            IssuedAt     = Timestamp.FromDateTimeOffset(credential.IssuedAt),
            ExpiresAt    = Timestamp.FromDateTimeOffset(credential.ExpiresAt),
            Ttl          = Duration.FromTimeSpan(ttl),
        };
    }

    // ── ValidateKubeconfigCredential ──────────────────────────────────────────

    public override async Task<ValidateKubeconfigCredentialResponse> ValidateKubeconfigCredential(
        ValidateKubeconfigCredentialRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Invalid(InvalidationReason.NotFound);

        if (!Guid.TryParse(request.ClusterId, out var clusterId))
            return Invalid(InvalidationReason.NotFound);

        var hash       = HashToken(request.Token);
        var credential = await db.AccessTokens
            .Find(t => t.TokenHash == hash && t.Kind == DomainCredentialKind.UserKubeconfig)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (credential is null)
            return Invalid(InvalidationReason.NotFound);

        if (credential.ClusterId != clusterId)
            return Invalid(InvalidationReason.WrongCluster);

        if (credential.IsRevoked)
            return Invalid(InvalidationReason.Revoked);

        if (credential.IsExpired)
            return Invalid(InvalidationReason.Expired);

        // Look up the owning user for principal info.
        User? user = null;
        if (credential.UserId.HasValue)
        {
            user = await db.Users
                .Find(u => u.Id == credential.UserId.Value)
                .FirstOrDefaultAsync(context.CancellationToken);
        }

        return new ValidateKubeconfigCredentialResponse
        {
            Valid        = true,
            CredentialId = credential.Id.ToString(),
            ExpiresAt    = Timestamp.FromDateTimeOffset(credential.ExpiresAt),
            Principal    = user is not null
                ? new Principal { Subject = user.KeycloakSubject, DisplayName = user.DisplayName ?? string.Empty }
                : new Principal(),
        };

        static ValidateKubeconfigCredentialResponse Invalid(InvalidationReason reason) =>
            new() { Valid = false, Reason = reason };
    }

    // ── IssueAgentCredential ──────────────────────────────────────────────────

    public override async Task<IssueAgentCredentialResponse> IssueAgentCredential(
        IssueAgentCredentialRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var clusterId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "cluster_id must be a valid UUID"));

        var cluster = await db.Clusters
            .Find(c => c.Id == clusterId)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (cluster is null)
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Cluster {request.ClusterId} not found"));

        // Verify the one-time bootstrap token.
        var providedHash = HashToken(request.BootstrapToken);
        if (cluster.BootstrapTokenHash is null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(cluster.BootstrapTokenHash),
                Encoding.UTF8.GetBytes(providedHash)))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Invalid bootstrap token."));
        }

        // Verify the public key matches what was submitted at registration.
        // Skip if neither side provided a key (local dev convenience).
        if (!string.IsNullOrEmpty(cluster.AgentPublicKeyPem) &&
            cluster.AgentPublicKeyPem != request.AgentPublicKeyPem)
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Agent public key does not match the registered key."));

        // Consume the bootstrap token — it is single-use.
        var clearBootstrap = Builders<Domain.Cluster>.Update
            .Set(c => c.BootstrapTokenHash, null!);
        await db.Clusters.UpdateOneAsync(
            c => c.Id == clusterId, clearBootstrap,
            cancellationToken: context.CancellationToken);

        var rawToken = GenerateToken();
        var now      = DateTimeOffset.UtcNow;

        // Agent credentials are long-lived (1 year by default).
        var credential = new DomainAccessToken
        {
            Id        = Guid.NewGuid(),
            Kind      = DomainCredentialKind.Agent,
            TokenHash = HashToken(rawToken),
            ClusterId = clusterId,
            IssuedAt  = now,
            ExpiresAt = now.AddYears(1),
        };
        await db.AccessTokens.InsertOneAsync(credential, cancellationToken: context.CancellationToken);

        logger.LogInformation(
            "Agent credential {Id} issued for cluster {ClusterId}", credential.Id, clusterId);

        return new IssueAgentCredentialResponse
        {
            CredentialId = credential.Id.ToString(),
            Token        = rawToken,
            IssuedAt     = Timestamp.FromDateTimeOffset(credential.IssuedAt),
            ExpiresAt    = Timestamp.FromDateTimeOffset(credential.ExpiresAt),
        };
    }

    // ── RotateAgentCredential ─────────────────────────────────────────────────

    public override async Task<RotateAgentCredentialResponse> RotateAgentCredential(
        RotateAgentCredentialRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var clusterId))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "cluster_id must be a valid UUID"));

        var currentHash = HashToken(request.CurrentToken);
        var existing    = await db.AccessTokens
            .Find(t => t.TokenHash == currentHash &&
                       t.ClusterId  == clusterId  &&
                       t.Kind       == DomainCredentialKind.Agent)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (existing is null || !existing.IsValid)
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Current token is invalid or expired."));

        // Revoke old credential and issue a fresh one.
        var revokeUpdate = Builders<DomainAccessToken>.Update
            .Set(t => t.RevokedAt, DateTimeOffset.UtcNow)
            .Set(t => t.RevokedReason, "rotated");
        await db.AccessTokens.UpdateOneAsync(
            t => t.Id == existing.Id, revokeUpdate,
            cancellationToken: context.CancellationToken);

        var rawToken = GenerateToken();
        var now      = DateTimeOffset.UtcNow;

        var newCredential = new DomainAccessToken
        {
            Id        = Guid.NewGuid(),
            Kind      = DomainCredentialKind.Agent,
            TokenHash = HashToken(rawToken),
            ClusterId = clusterId,
            IssuedAt  = now,
            ExpiresAt = now.AddYears(1),
        };
        await db.AccessTokens.InsertOneAsync(newCredential, cancellationToken: context.CancellationToken);

        return new RotateAgentCredentialResponse
        {
            CredentialId = newCredential.Id.ToString(),
            Token        = rawToken,
            IssuedAt     = Timestamp.FromDateTimeOffset(newCredential.IssuedAt),
            ExpiresAt    = Timestamp.FromDateTimeOffset(newCredential.ExpiresAt),
        };
    }

    // ── RevokeCredential ──────────────────────────────────────────────────────

    public override async Task<RevokeCredentialResponse> RevokeCredential(
        RevokeCredentialRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.CredentialId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "credential_id must be a valid UUID"));

        var now = DateTimeOffset.UtcNow;
        var update = Builders<DomainAccessToken>.Update
            .Set(t => t.RevokedAt, now)
            .Set(t => t.RevokedReason, request.Reason);

        var result = await db.AccessTokens.UpdateOneAsync(
            t => t.Id == id, update,
            cancellationToken: context.CancellationToken);

        if (result.MatchedCount == 0)
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Credential {request.CredentialId} not found"));

        return new RevokeCredentialResponse
        {
            Revoked   = true,
            RevokedAt = Timestamp.FromDateTimeOffset(now),
        };
    }

    // -------------------------------------------------------------------------

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
