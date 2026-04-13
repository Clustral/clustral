using System.IdentityModel.Tokens.Jwt;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Results;

namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Validates proxy requests using the internal JWT issued by the gateway.
/// The gateway has already validated the external token (OIDC or kubeconfig JWT).
/// This service extracts the identity and checks revocation for kubeconfig credentials.
/// </summary>
public sealed class ProxyAuthService(
    IAccessTokenRepository accessTokens)
{
    public async Task<Result<ProxyIdentity>> AuthenticateAsync(
        Guid clusterId, string? internalToken, CancellationToken ct = default)
    {
        // The gateway validates all tokens (OIDC + kubeconfig JWT) and issues
        // an internal JWT with the extracted claims. Read identity from it.
        if (!string.IsNullOrEmpty(internalToken))
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(internalToken))
            {
                var jwt = handler.ReadJwtToken(internalToken);
                return await ValidateFromInternalJwt(jwt, clusterId, ct);
            }
        }

        return ResultErrors.InvalidCredential();
    }

    private async Task<Result<ProxyIdentity>> ValidateFromInternalJwt(
        JwtSecurityToken jwt, Guid clusterId, CancellationToken ct)
    {
        var sub = jwt.Subject
            ?? jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
            ?? jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sub))
            return ResultErrors.InvalidCredential();

        // Check if this is a kubeconfig credential (has cluster_id + jti claims)
        var clusterIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "cluster_id")?.Value;
        var jtiClaim = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
        var kindClaim = jwt.Claims.FirstOrDefault(c => c.Type == "kind")?.Value;

        if (kindClaim == KubeconfigJwtService.KindValue)
        {
            // Kubeconfig credential — validate cluster scope + check revocation
            if (!Guid.TryParse(sub, out var userId))
                return ResultErrors.InvalidCredential();

            if (!string.IsNullOrEmpty(clusterIdClaim) && Guid.TryParse(clusterIdClaim, out var tokenClusterId))
            {
                if (tokenClusterId != clusterId)
                    return ResultError.Forbidden("Credential is not valid for this cluster.");
            }

            // Check revocation + expiry by credential ID (jti).
            // Defense in depth: the gateway already validated the JWT exp claim,
            // but allows up to 30s of ClockSkew. The DB record's ExpiresAt is
            // precise — checking it here closes the skew window so a credential
            // becomes unusable the instant its recorded expiry passes.
            if (!string.IsNullOrEmpty(jtiClaim) && Guid.TryParse(jtiClaim, out var credentialId))
            {
                var credential = await accessTokens.GetByIdAsync(credentialId, ct);
                if (credential is not null && (credential.IsRevoked || credential.IsExpired))
                    return ResultErrors.InvalidCredential();

                return new ProxyIdentity(userId, clusterId, credentialId);
            }

            return new ProxyIdentity(userId, clusterId, Guid.Empty);
        }

        // OIDC-authenticated request (from Web UI) — sub is OIDC subject string.
        // The user was already authenticated by the gateway. We trust the identity.
        // Note: sub is not a GUID for OIDC users, but ProxyIdentity needs a GUID userId.
        // The impersonation resolver will look up the user by the claims.
        if (Guid.TryParse(sub, out var oidcUserId))
            return new ProxyIdentity(oidcUserId, clusterId, Guid.Empty);

        return ResultErrors.InvalidCredential();
    }
}
