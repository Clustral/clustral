using System.IdentityModel.Tokens.Jwt;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Results;

namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Validates proxy bearer tokens and returns the authenticated identity.
/// Supports kubeconfig JWTs (ES256), gateway-authenticated requests
/// (internal JWT), and legacy random tokens (SHA-256 hash lookup).
/// </summary>
public sealed class ProxyAuthService(
    IAccessTokenRepository accessTokens,
    TokenHashingService tokenHasher,
    IUserRepository users,
    KubeconfigJwtService? kubeconfigJwt = null)
{
    public async Task<Result<ProxyIdentity>> AuthenticateAsync(
        string bearerToken, Guid clusterId, CancellationToken ct = default,
        string? internalToken = null)
    {
        // 1. Try kubeconfig JWT validation (kubectl requests with ES256-signed token)
        if (kubeconfigJwt is not null && IsJwt(bearerToken))
        {
            var principal = kubeconfigJwt.Validate(bearerToken);
            if (principal is not null)
                return await ValidateKubeconfigJwt(principal, clusterId, ct);
        }

        // 2. If request came through gateway with an internal JWT,
        //    the bearer token is an OIDC JWT (from Web UI or CLI).
        //    The gateway already validated it. Extract userId from the
        //    internal token's sub claim and find the user.
        if (!string.IsNullOrEmpty(internalToken) && IsJwt(internalToken))
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(internalToken))
            {
                var jwt = handler.ReadJwtToken(internalToken);
                var sub = jwt.Subject
                    ?? jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

                if (!string.IsNullOrEmpty(sub))
                {
                    // The internal JWT sub is the OIDC subject (a string, not a GUID).
                    // Look up the user by their Keycloak subject.
                    var user = await users.GetBySubjectAsync(sub, ct);
                    if (user is not null)
                        return new ProxyIdentity(user.Id, clusterId, Guid.Empty);
                }
            }
        }

        // 3. Fallback: legacy random token (SHA-256 hash lookup)
        return await ValidateLegacyToken(bearerToken, clusterId, ct);
    }

    private async Task<Result<ProxyIdentity>> ValidateKubeconfigJwt(
        System.Security.Claims.ClaimsPrincipal principal, Guid clusterId, CancellationToken ct)
    {
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

    private static bool IsJwt(string token) =>
        token.Count(c => c == '.') == 2 && token.Length > 50;
}
