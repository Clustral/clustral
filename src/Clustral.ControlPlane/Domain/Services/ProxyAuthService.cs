using System.IdentityModel.Tokens.Jwt;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Results;

namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Validates proxy bearer tokens and returns the authenticated identity.
/// Supports both kubeconfig JWTs (ES256, validated cryptographically)
/// and legacy random tokens (SHA-256 hash lookup in MongoDB).
/// </summary>
public sealed class ProxyAuthService(
    IAccessTokenRepository accessTokens,
    TokenHashingService tokenHasher,
    KubeconfigJwtService? kubeconfigJwt = null)
{
    /// <summary>
    /// Authenticates a bearer token for a specific cluster.
    /// Returns the user identity or a descriptive error.
    /// </summary>
    public async Task<Result<ProxyIdentity>> AuthenticateAsync(
        string bearerToken, Guid clusterId, CancellationToken ct = default)
    {
        // Try kubeconfig JWT validation first (if service is available)
        if (kubeconfigJwt is not null && IsJwt(bearerToken))
        {
            var principal = kubeconfigJwt.Validate(bearerToken);
            if (principal is not null)
                return await ValidateKubeconfigJwt(principal, clusterId, ct);
        }

        // Fallback: legacy random token (SHA-256 hash lookup)
        return await ValidateLegacyToken(bearerToken, clusterId, ct);
    }

    private async Task<Result<ProxyIdentity>> ValidateKubeconfigJwt(
        System.Security.Claims.ClaimsPrincipal principal, Guid clusterId, CancellationToken ct)
    {
        // Extract claims from JWT
        var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst("sub")?.Value;
        var jtiClaim = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
            ?? principal.FindFirst("jti")?.Value;
        var clusterIdClaim = principal.FindFirst(KubeconfigJwtService.ClusterIdClaim)?.Value;

        if (string.IsNullOrEmpty(subClaim) || !Guid.TryParse(subClaim, out var userId))
            return ResultErrors.InvalidCredential();

        if (string.IsNullOrEmpty(clusterIdClaim) || !Guid.TryParse(clusterIdClaim, out var tokenClusterId))
            return ResultErrors.InvalidCredential();

        if (tokenClusterId != clusterId)
            return ResultError.Forbidden("Credential is not valid for this cluster.");

        // Check revocation in MongoDB (by credential ID / jti)
        if (!string.IsNullOrEmpty(jtiClaim) && Guid.TryParse(jtiClaim, out var credentialId))
        {
            var credential = await accessTokens.GetByIdAsync(credentialId, ct);
            if (credential is not null && credential.IsRevoked)
                return ResultErrors.InvalidCredential();
        }

        return new ProxyIdentity(userId, clusterId,
            Guid.TryParse(jtiClaim, out var cid) ? cid : Guid.Empty);
    }

    private async Task<Result<ProxyIdentity>> ValidateLegacyToken(
        string bearerToken, Guid clusterId, CancellationToken ct)
    {
        var tokenHash = tokenHasher.HashToken(bearerToken);
        var credential = await accessTokens.GetByHashAsync(tokenHash, ct);

        if (credential is null || !credential.IsValid || credential.Kind != CredentialKind.UserKubeconfig)
            return ResultErrors.InvalidCredential();

        if (credential.ClusterId != clusterId)
            return ResultError.Forbidden("Credential is not valid for this cluster.");

        if (!credential.UserId.HasValue)
            return ResultErrors.InvalidCredential();

        return new ProxyIdentity(credential.UserId.Value, clusterId, credential.Id);
    }

    /// <summary>
    /// Quick check if a string looks like a JWT (three dot-separated base64 segments).
    /// </summary>
    private static bool IsJwt(string token) =>
        token.Count(c => c == '.') == 2 && token.Length > 50;
}
