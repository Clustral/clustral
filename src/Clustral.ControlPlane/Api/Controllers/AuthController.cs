using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Api.Controllers;

/// <summary>
/// Issues and revokes Clustral kubeconfig credentials.
/// Called by <c>clustral kube login</c> and <c>clustral logout</c>.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Authorize]
public sealed class AuthController(
    ClustralDb db,
    IOptions<KeycloakOptions> keycloakOptions,
    ILogger<AuthController> logger)
    : ControllerBase
{
    // POST /api/v1/auth/kubeconfig-credential
    /// <summary>
    /// Exchanges the caller's Keycloak OIDC token (in the Authorization header)
    /// for a short-lived Clustral kubeconfig bearer token scoped to one cluster.
    /// </summary>
    [HttpPost("kubeconfig-credential")]
    [ProducesResponseType<IssueKubeconfigCredentialResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IssueKubeconfigCredential(
        [FromBody] IssueKubeconfigCredentialRequest request,
        CancellationToken ct)
    {
        // 1. Verify the cluster exists.
        var cluster = await db.Clusters
            .Find(c => c.Id == request.ClusterId)
            .FirstOrDefaultAsync(ct);

        if (cluster is null)
            return NotFound(new { error = $"Cluster {request.ClusterId} not found." });

        // 2. Determine TTL — cap at the configured maximum.
        var opts = keycloakOptions.Value;
        var ttl  = opts.DefaultKubeconfigCredentialTtl;

        if (!string.IsNullOrEmpty(request.RequestedTtl) &&
            TimeSpan.TryParse(request.RequestedTtl, out var requestedTtl))
        {
            ttl = requestedTtl < opts.MaxKubeconfigCredentialTtl
                ? requestedTtl
                : opts.MaxKubeconfigCredentialTtl;
        }

        // 3. Upsert user record from the JWT claims.
        var subject     = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                       ?? User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                       ?? string.Empty;
        var displayName = User.Claims.FirstOrDefault(c => c.Type == "name")?.Value
                       ?? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                       ?? User.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
        var email       = User.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                       ?? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
                       ?? User.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;

        var existingUser = await db.Users
            .Find(u => u.KeycloakSubject == subject)
            .FirstOrDefaultAsync(ct);

        Guid userId;
        if (existingUser is null)
        {
            var newUser = new Domain.User
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
            var update = Builders<Domain.User>.Update
                .Set(u => u.DisplayName, displayName)
                .Set(u => u.Email, email)
                .Set(u => u.LastSeenAt, DateTimeOffset.UtcNow);
            await db.Users.UpdateOneAsync(u => u.Id == existingUser.Id, update, cancellationToken: ct);
        }

        // 4. Generate a cryptographically random bearer token.
        var rawToken  = GenerateToken();
        var tokenHash = HashToken(rawToken);
        var now       = DateTimeOffset.UtcNow;

        var expiresAt = now + ttl;

        // Cap credential TTL to JIT grant expiry if user has no static assignment.
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

        var response = new IssueKubeconfigCredentialResponse(
            CredentialId: credential.Id,
            Token:        rawToken,
            IssuedAt:     credential.IssuedAt,
            ExpiresAt:    credential.ExpiresAt,
            Subject:      subject,
            DisplayName:  displayName);

        return CreatedAtAction(nameof(IssueKubeconfigCredential), response);
    }

    // DELETE /api/v1/auth/credentials/{id}
    [HttpDelete("credentials/{id:guid}")]
    [ProducesResponseType<RevokeCredentialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeCredential(
        Guid id,
        [FromBody] RevokeCredentialRequest? request,
        CancellationToken ct)
    {
        var credential = await db.AccessTokens
            .Find(t => t.Id == id)
            .FirstOrDefaultAsync(ct);

        if (credential is null)
            return NotFound();

        // Users may only revoke their own credentials.
        var subject = User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (credential.UserId.HasValue)
        {
            var owner = await db.Users
                .Find(u => u.Id == credential.UserId.Value)
                .FirstOrDefaultAsync(ct);
            if (owner?.KeycloakSubject != subject)
                return Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessToken>.Update
            .Set(t => t.RevokedAt, now)
            .Set(t => t.RevokedReason, request?.Reason);
        await db.AccessTokens.UpdateOneAsync(t => t.Id == id, update, cancellationToken: ct);

        logger.LogInformation("Credential {CredentialId} revoked by {Subject}", id, subject);

        return Ok(new RevokeCredentialResponse(Revoked: true, RevokedAt: now));
    }

    // POST /api/v1/auth/revoke-by-token
    /// <summary>
    /// Revokes a kubeconfig credential by its raw token value.
    /// Used by the CLI during <c>clustral logout</c>.
    /// </summary>
    [HttpPost("revoke-by-token")]
    [ProducesResponseType<RevokeCredentialResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeByToken(
        [FromBody] RevokeByTokenRequest request,
        CancellationToken ct)
    {
        var hash = HashToken(request.Token);
        var credential = await db.AccessTokens
            .Find(t => t.TokenHash == hash && t.Kind == CredentialKind.UserKubeconfig)
            .FirstOrDefaultAsync(ct);

        if (credential is null)
            return NotFound();

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessToken>.Update
            .Set(t => t.RevokedAt, now)
            .Set(t => t.RevokedReason, "logout");
        await db.AccessTokens.UpdateOneAsync(t => t.Id == credential.Id, update, cancellationToken: ct);

        logger.LogInformation("Credential {CredentialId} revoked via logout", credential.Id);

        return Ok(new RevokeCredentialResponse(Revoked: true, RevokedAt: now));
    }

    // -------------------------------------------------------------------------

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    internal static string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
