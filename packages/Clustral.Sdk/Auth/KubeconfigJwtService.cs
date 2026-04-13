using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.Sdk.Auth;

/// <summary>
/// Issues and validates kubeconfig credential JWTs.
/// Thin wrapper over <see cref="Es256JwtService"/> with kubeconfig-specific claims.
/// </summary>
public sealed class KubeconfigJwtService
{
    private readonly Es256JwtService _jwt;

    public const string IssuerName = "clustral-controlplane";
    public const string AudienceName = "clustral-kubeconfig";
    public const string KindClaim = "kind";
    public const string ClusterIdClaim = "cluster_id";
    public const string KindValue = "kubeconfig";

    public static KubeconfigJwtService ForSigning(string privateKeyPem) =>
        new(Es256JwtService.Create(privateKeyPem, IssuerName, AudienceName));

    public static KubeconfigJwtService ForValidation(string publicKeyPem) =>
        new(Es256JwtService.ForValidation(publicKeyPem, IssuerName, AudienceName));

    private KubeconfigJwtService(Es256JwtService jwt) => _jwt = jwt;

    public string Issue(Guid credentialId, Guid userId, Guid clusterId, DateTimeOffset expiresAt) =>
        _jwt.Issue(
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, credentialId.ToString()),
            new Claim(ClusterIdClaim, clusterId.ToString()),
            new Claim(KindClaim, KindValue),
        ], expiresAt);

    public ClaimsPrincipal? Validate(string token) => _jwt.Validate(token);

    public SecurityKey GetSecurityKey() => _jwt.GetSecurityKey();

    public TokenValidationParameters GetValidationParameters() => _jwt.GetValidationParameters();
}
