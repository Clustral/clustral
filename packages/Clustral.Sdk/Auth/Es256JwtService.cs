using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.Sdk.Auth;

/// <summary>
/// Unified ES256 JWT service. Handles signing and validation for any
/// JWT type (internal gateway tokens, kubeconfig credentials, etc.).
/// Each instance is configured with an issuer, audience, and key pair.
/// </summary>
public sealed class Es256JwtService
{
    private readonly ECDsa? _signingKey;
    private readonly ECDsa? _validationKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <summary>Creates a service that can both sign and validate.</summary>
    public static Es256JwtService Create(string privateKeyPem, string issuer, string audience) =>
        new(LoadKey(privateKeyPem), LoadKey(privateKeyPem), issuer, audience);

    /// <summary>Creates a service that can only sign (no validation key).</summary>
    public static Es256JwtService ForSigning(string privateKeyPem, string issuer, string audience) =>
        new(LoadKey(privateKeyPem), null, issuer, audience);

    /// <summary>Creates a service that can only validate (no signing key).</summary>
    public static Es256JwtService ForValidation(string publicKeyPem, string issuer, string audience) =>
        new(null, LoadKey(publicKeyPem), issuer, audience);

    private Es256JwtService(ECDsa? signingKey, ECDsa? validationKey, string issuer, string audience)
    {
        _signingKey = signingKey;
        _validationKey = validationKey;
        _issuer = issuer;
        _audience = audience;
    }

    /// <summary>Signs a JWT with the given claims and expiry.</summary>
    public string Issue(IEnumerable<Claim> claims, DateTimeOffset expiresAt)
    {
        if (_signingKey is null)
            throw new InvalidOperationException("No signing key configured.");

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new ECDsaSecurityKey(_signingKey), SecurityAlgorithms.EcdsaSha256));

        return _handler.WriteToken(token);
    }

    /// <summary>
    /// Validates a JWT and returns the claims principal. Returns null on failure.
    /// </summary>
    public ClaimsPrincipal? Validate(string token)
    {
        if (_validationKey is null)
            throw new InvalidOperationException("No validation key configured.");

        try
        {
            return _handler.ValidateToken(token, GetValidationParameters(), out _);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the ES256 security key for use in JwtBearer config.</summary>
    public SecurityKey GetSecurityKey() =>
        new ECDsaSecurityKey(_validationKey ?? _signingKey
            ?? throw new InvalidOperationException("No key configured."));

    /// <summary>Returns validation parameters for JwtBearer middleware.</summary>
    public TokenValidationParameters GetValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _issuer,
        ValidateAudience = true,
        ValidAudience = _audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        IssuerSigningKey = new ECDsaSecurityKey(_validationKey
            ?? throw new InvalidOperationException("No validation key configured.")),
    };

    public string Issuer => _issuer;
    public string Audience => _audience;

    private static ECDsa LoadKey(string pem)
    {
        // cert-manager outputs X.509 certificates (-----BEGIN CERTIFICATE-----).
        // Extract the ECDSA key from the certificate so both raw PEM keys and
        // cert-manager Secrets work without template-side hacks.
        if (pem.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            var cert = X509Certificate2.CreateFromPem(pem);
            return cert.GetECDsaPublicKey()
                   ?? throw new InvalidOperationException(
                       "Certificate does not contain an ECDSA public key.");
        }

        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }
}
