using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Clustral.Sdk.Crypto;

/// <summary>
/// Issues and validates RS256 JWTs for agent authentication.
/// Uses the CA RSA key for signing — no separate JWT key needed.
/// </summary>
public sealed class JwtIssuer
{
    private const string Issuer = "clustral-controlplane";

    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly CertificateAuthorityOptions _options;

    public JwtIssuer(CertificateAuthority ca, CertificateAuthorityOptions options)
    {
        ArgumentNullException.ThrowIfNull(ca);
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var rsaKey = new RsaSecurityKey(ca.GetSigningKey());
        _signingCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = rsaKey,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>
    /// Issues a JWT for an agent with the specified claims.
    /// </summary>
    public string IssueToken(
        string agentId,
        string orgId,
        string clusterId,
        IReadOnlyList<string> allowedRpcs,
        int tokenVersion)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new("agent_id", agentId),
            new("org_id", orgId),
            new("cluster_id", clusterId),
            new("token_version", tokenVersion.ToString(), ClaimValueTypes.Integer32),
            new("allowed_rpcs", JsonSerializer.Serialize(allowedRpcs), JsonClaimValueTypes.JsonArray),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            claims: claims,
            notBefore: now,
            expires: now.AddDays(_options.JwtValidityDays),
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Returns the expiry time of the issued JWT.
    /// </summary>
    public DateTimeOffset GetTokenExpiry()
    {
        return DateTimeOffset.UtcNow.AddDays(_options.JwtValidityDays);
    }

    /// <summary>
    /// Validates a JWT and returns the claims principal.
    /// Throws <see cref="SecurityTokenException"/> on failure.
    /// </summary>
    public ClaimsPrincipal ValidateToken(string jwt)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(jwt, _validationParameters, out _);
    }

    /// <summary>
    /// Extracts the agent_id claim from a validated claims principal.
    /// </summary>
    public static string? GetAgentId(ClaimsPrincipal principal)
        => principal.FindFirstValue("agent_id");

    /// <summary>
    /// Extracts the cluster_id claim from a validated claims principal.
    /// </summary>
    public static string? GetClusterId(ClaimsPrincipal principal)
        => principal.FindFirstValue("cluster_id");

    /// <summary>
    /// Extracts the token_version claim from a validated claims principal.
    /// </summary>
    public static int GetTokenVersion(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("token_version");
        return int.TryParse(value, out var version) ? version : 0;
    }

    /// <summary>
    /// Extracts the allowed_rpcs claim from a validated claims principal.
    /// </summary>
    public static IReadOnlyList<string> GetAllowedRpcs(ClaimsPrincipal principal)
    {
        // The JWT handler may split JSON arrays into individual claims,
        // so we collect all values for the "allowed_rpcs" claim type.
        return principal.FindAll("allowed_rpcs")
            .Select(c => c.Value)
            .ToList();
    }
}
