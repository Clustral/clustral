using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.Sdk.Auth;

/// <summary>
/// Issues short-lived internal JWTs for gateway-to-downstream communication.
/// Thin wrapper over <see cref="Es256JwtService"/> with filtered claims.
/// </summary>
public sealed class InternalJwtService
{
    private readonly Es256JwtService _jwt;
    private readonly TimeSpan _tokenLifetime;

    public const string IssuerName = "clustral-gateway";
    public const string AudienceName = "clustral-internal";

    private static readonly HashSet<string> AllowedClaimTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ClaimTypes.NameIdentifier, "sub",
        ClaimTypes.Email, "email",
        ClaimTypes.Name, "name",
        "preferred_username",
        "cluster_id", "jti", "kind",
    };

    public static InternalJwtService ForSigning(string privateKeyPem, TimeSpan? tokenLifetime = null) =>
        new(Es256JwtService.ForSigning(privateKeyPem, IssuerName, AudienceName),
            tokenLifetime ?? TimeSpan.FromSeconds(30));

    public static InternalJwtService ForValidation(string publicKeyPem) =>
        new(Es256JwtService.ForValidation(publicKeyPem, IssuerName, AudienceName),
            TimeSpan.Zero);

    private InternalJwtService(Es256JwtService jwt, TimeSpan tokenLifetime)
    {
        _jwt = jwt;
        _tokenLifetime = tokenLifetime;
    }

    public string Issue(IEnumerable<Claim> claims) =>
        _jwt.Issue(
            claims.Where(c => AllowedClaimTypes.Contains(c.Type)),
            DateTimeOffset.UtcNow.Add(_tokenLifetime));

    public TokenValidationParameters GetValidationParameters() =>
        _jwt.GetValidationParameters();
}
