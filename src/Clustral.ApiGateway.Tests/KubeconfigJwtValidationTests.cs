using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Clustral.Sdk.Auth;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

namespace Clustral.ApiGateway.Tests;

/// <summary>
/// Validates the gateway's strict issuer/audience enforcement for kubeconfig
/// JWTs. These tests exercise the <see cref="KubeconfigJwtService"/> directly
/// (the same validation parameters the gateway's JwtBearer scheme uses).
/// </summary>
public sealed class KubeconfigJwtValidationTests(ITestOutputHelper output)
{
    private static (string privatePem, string publicPem) NewKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportECPrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public void Validate_AcceptsWellFormedKubeconfigToken()
    {
        var (priv, pub) = NewKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);
        var validator = KubeconfigJwtService.ForValidation(pub);

        var token = signer.Issue(
            credentialId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            clusterId: Guid.NewGuid(),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var principal = validator.Validate(token);
        principal.Should().NotBeNull();
        output.WriteLine($"Valid: {principal!.Identity!.IsAuthenticated}");
    }

    [Fact]
    public void Validate_RejectsTokenWithWrongIssuer()
    {
        var (priv, pub) = NewKeyPair();

        // Hand-craft a token signed with our key but wrong issuer.
        using var key = ECDsa.Create();
        key.ImportFromPem(priv);
        var handler = new JwtSecurityTokenHandler();
        var raw = handler.WriteToken(new JwtSecurityToken(
            issuer: "evil-issuer",
            audience: KubeconfigJwtService.AudienceName,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new ECDsaSecurityKey(key),
                SecurityAlgorithms.EcdsaSha256)));

        var validator = KubeconfigJwtService.ForValidation(pub);
        var principal = validator.Validate(raw);
        principal.Should().BeNull("wrong issuer must be rejected even if signature is valid");
    }

    [Fact]
    public void Validate_RejectsTokenWithWrongAudience()
    {
        var (priv, pub) = NewKeyPair();

        using var key = ECDsa.Create();
        key.ImportFromPem(priv);
        var handler = new JwtSecurityTokenHandler();
        var raw = handler.WriteToken(new JwtSecurityToken(
            issuer: KubeconfigJwtService.IssuerName,
            audience: "some-other-audience",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new ECDsaSecurityKey(key),
                SecurityAlgorithms.EcdsaSha256)));

        var validator = KubeconfigJwtService.ForValidation(pub);
        var principal = validator.Validate(raw);
        principal.Should().BeNull("wrong audience must be rejected");
    }

    [Fact]
    public void Validate_RejectsTokenSignedByDifferentKey()
    {
        var (_, pub) = NewKeyPair();
        var (otherPriv, _) = NewKeyPair();

        var evilSigner = KubeconfigJwtService.ForSigning(otherPriv);
        var token = evilSigner.Issue(
            credentialId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            clusterId: Guid.NewGuid(),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var validator = KubeconfigJwtService.ForValidation(pub);
        var principal = validator.Validate(token);
        principal.Should().BeNull("token signed by attacker's key must be rejected");
    }

    [Fact]
    public void Validate_RejectsExpiredToken()
    {
        var (priv, pub) = NewKeyPair();

        // Hand-craft a well-formed token that expired well outside the 30s
        // ClockSkew window (issuer still sets notBefore in the past so
        // Expires > NotBefore passes the SDK's sanity check).
        using var key = ECDsa.Create();
        key.ImportFromPem(priv);
        var handler = new JwtSecurityTokenHandler();
        var raw = handler.WriteToken(new JwtSecurityToken(
            issuer: KubeconfigJwtService.IssuerName,
            audience: KubeconfigJwtService.AudienceName,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1), // expired 1h ago — well past ClockSkew
            signingCredentials: new SigningCredentials(new ECDsaSecurityKey(key),
                SecurityAlgorithms.EcdsaSha256)));

        var validator = KubeconfigJwtService.ForValidation(pub);
        var principal = validator.Validate(raw);
        principal.Should().BeNull("expired token must be rejected");
    }

    [Fact]
    public void ValidationParameters_PinIssuerAndAudience()
    {
        var (_, pub) = NewKeyPair();
        var validator = KubeconfigJwtService.ForValidation(pub);
        var p = validator.GetValidationParameters();

        p.ValidateIssuer.Should().BeTrue();
        p.ValidIssuer.Should().Be(KubeconfigJwtService.IssuerName);
        p.ValidateAudience.Should().BeTrue();
        p.ValidAudience.Should().Be(KubeconfigJwtService.AudienceName);
        p.ValidateLifetime.Should().BeTrue();
        p.ValidAlgorithms.Should().Contain(SecurityAlgorithms.EcdsaSha256);
    }

    [Fact]
    public void Issue_IncludesKindClaim_SoGatewayCanRoute()
    {
        var (priv, _) = NewKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);
        var token = signer.Issue(
            credentialId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            clusterId: Guid.NewGuid(),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var kind = jwt.Claims.FirstOrDefault(c => c.Type == KubeconfigJwtService.KindClaim)?.Value;
        kind.Should().Be(KubeconfigJwtService.KindValue,
            "gateway's policy scheme dispatches on the 'kind' claim");
    }
}
