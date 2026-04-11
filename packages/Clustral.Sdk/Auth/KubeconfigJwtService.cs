using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.Sdk.Auth;

/// <summary>
/// Issues and validates kubeconfig credential JWTs (ES256).
/// The ControlPlane signs with the private key when issuing credentials.
/// The API Gateway validates with the public key.
/// </summary>
public sealed class KubeconfigJwtService
{
    private readonly ECDsa? _signingKey;
    private readonly ECDsa? _validationKey;
    private readonly JwtSecurityTokenHandler _handler = new();

    public const string Issuer = "clustral-controlplane";
    public const string Audience = "clustral-kubeconfig";
    public const string KindClaim = "kind";
    public const string ClusterIdClaim = "cluster_id";
    public const string KindValue = "kubeconfig";

    public static KubeconfigJwtService ForSigning(string privateKeyPem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return new KubeconfigJwtService(signingKey: key, validationKey: null);
    }

    public static KubeconfigJwtService ForValidation(string publicKeyPem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        return new KubeconfigJwtService(signingKey: null, validationKey: key);
    }

    private KubeconfigJwtService(ECDsa? signingKey, ECDsa? validationKey)
    {
        _signingKey = signingKey;
        _validationKey = validationKey;
    }

    public string Issue(Guid credentialId, Guid userId, Guid clusterId, DateTimeOffset expiresAt)
    {
        if (_signingKey is null)
            throw new InvalidOperationException(
                "KubeconfigJwtService was created for validation only (no private key).");

        var credentials = new SigningCredentials(
            new ECDsaSecurityKey(_signingKey), SecurityAlgorithms.EcdsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, credentialId.ToString()),
            new Claim(ClusterIdClaim, clusterId.ToString()),
            new Claim(KindClaim, KindValue),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return _handler.WriteToken(token);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        if (_validationKey is null)
            throw new InvalidOperationException(
                "KubeconfigJwtService was created for signing only (no public key).");

        try
        {
            return _handler.ValidateToken(token, GetValidationParameters(), out _);
        }
        catch
        {
            return null;
        }
    }

    public SecurityKey GetSecurityKey()
    {
        if (_validationKey is not null)
            return new ECDsaSecurityKey(_validationKey);
        if (_signingKey is not null)
            return new ECDsaSecurityKey(_signingKey);
        throw new InvalidOperationException("No key available.");
    }

    public TokenValidationParameters GetValidationParameters()
    {
        if (_validationKey is null)
            throw new InvalidOperationException(
                "KubeconfigJwtService was created for signing only (no public key).");

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKey = new ECDsaSecurityKey(_validationKey),
        };
    }
}
