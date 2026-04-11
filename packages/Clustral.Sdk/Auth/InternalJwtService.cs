using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.Sdk.Auth;

/// <summary>
/// Issues and validates short-lived internal JWTs (ES256) for
/// gateway-to-downstream service communication. The gateway signs
/// with the private key; downstream services validate with the
/// public key only.
/// </summary>
public sealed class InternalJwtService
{
    private readonly ECDsa? _signingKey;
    private readonly ECDsa? _validationKey;
    private readonly JwtSecurityTokenHandler _handler = new();
    private readonly TimeSpan _tokenLifetime;

    /// <summary>
    /// Creates an InternalJwtService for <b>issuing</b> tokens (gateway).
    /// Requires the ES256 private key.
    /// </summary>
    public static InternalJwtService ForSigning(string privateKeyPem, TimeSpan? tokenLifetime = null)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return new InternalJwtService(signingKey: key, validationKey: null,
            tokenLifetime ?? TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Creates an InternalJwtService for <b>validating</b> tokens (downstream).
    /// Requires only the ES256 public key.
    /// </summary>
    public static InternalJwtService ForValidation(string publicKeyPem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        return new InternalJwtService(signingKey: null, validationKey: key,
            TimeSpan.Zero);
    }

    private InternalJwtService(ECDsa? signingKey, ECDsa? validationKey, TimeSpan tokenLifetime)
    {
        _signingKey = signingKey;
        _validationKey = validationKey;
        _tokenLifetime = tokenLifetime;
    }

    /// <summary>
    /// Issues a short-lived internal JWT containing the given claims.
    /// Called by the gateway after OIDC validation.
    /// </summary>
    public string Issue(IEnumerable<Claim> claims)
    {
        if (_signingKey is null)
            throw new InvalidOperationException(
                "InternalJwtService was created for validation only (no private key).");

        var credentials = new SigningCredentials(
            new ECDsaSecurityKey(_signingKey), SecurityAlgorithms.EcdsaSha256);

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "clustral-gateway",
            audience: "clustral-internal",
            claims: claims.Where(c =>
                c.Type is ClaimTypes.NameIdentifier or "sub"
                    or ClaimTypes.Email or "email"
                    or ClaimTypes.Name or "name"
                    or "preferred_username"),
            notBefore: now,
            expires: now.Add(_tokenLifetime),
            signingCredentials: credentials);

        return _handler.WriteToken(token);
    }

    /// <summary>
    /// Returns <see cref="TokenValidationParameters"/> for downstream
    /// services to configure JwtBearer authentication.
    /// </summary>
    public TokenValidationParameters GetValidationParameters()
    {
        if (_validationKey is null)
            throw new InvalidOperationException(
                "InternalJwtService was created for signing only (no public key).");

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "clustral-gateway",
            ValidateAudience = true,
            ValidAudience = "clustral-internal",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKey = new ECDsaSecurityKey(_validationKey),
        };
    }
}
