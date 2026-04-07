using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;

namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Validates proxy bearer tokens and returns the authenticated identity.
/// Extracted from KubectlProxyMiddleware for testability and DDD alignment.
/// </summary>
public sealed class ProxyAuthService(
    IAccessTokenRepository accessTokens,
    TokenHashingService tokenHasher)
{
    /// <summary>
    /// Authenticates a bearer token for a specific cluster.
    /// Returns the user identity or a descriptive error.
    /// </summary>
    public async Task<Result<ProxyIdentity>> AuthenticateAsync(
        string bearerToken, Guid clusterId, CancellationToken ct = default)
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
}
