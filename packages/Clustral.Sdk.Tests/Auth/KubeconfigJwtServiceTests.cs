using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Clustral.Sdk.Auth;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Auth;

public sealed class KubeconfigJwtServiceTests(ITestOutputHelper output)
{
    private static (string privateKey, string publicKey) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportECPrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public void Issue_CreatesValidJwt_WithCorrectClaims()
    {
        var (priv, pub) = GenerateKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);
        var validator = KubeconfigJwtService.ForValidation(pub);

        var credentialId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);

        var token = signer.Issue(credentialId, userId, clusterId, expiresAt);
        output.WriteLine($"Token: {token[..60]}...");

        var principal = validator.Validate(token);
        principal.Should().NotBeNull();

        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            .Should().Be(userId.ToString());
        principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
            .Should().Be(credentialId.ToString());
        principal.FindFirst(KubeconfigJwtService.ClusterIdClaim)?.Value
            .Should().Be(clusterId.ToString());
        principal.FindFirst(KubeconfigJwtService.KindClaim)?.Value
            .Should().Be(KubeconfigJwtService.KindValue);
    }

    [Fact]
    public void Issue_SetsCorrectExpiry()
    {
        var (priv, _) = GenerateKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        var token = signer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), expiresAt);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var diff = Math.Abs((jwt.ValidTo - expiresAt.UtcDateTime).TotalSeconds);
        output.WriteLine($"Expiry diff: {diff}s");

        diff.Should().BeLessThan(2, "JWT expiry should match requested expiresAt");
    }

    [Fact]
    public void Validate_AcceptsValidToken()
    {
        var (priv, pub) = GenerateKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);
        var validator = KubeconfigJwtService.ForValidation(pub);

        var token = signer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1));

        var result = validator.Validate(token);
        result.Should().NotBeNull();
    }

    [Fact]
    public void Validate_RejectsDifferentKey()
    {
        var (priv, _) = GenerateKeyPair();
        var (_, otherPub) = GenerateKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);
        var validator = KubeconfigJwtService.ForValidation(otherPub);

        var token = signer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1));

        var result = validator.Validate(token);
        result.Should().BeNull("token signed with different key should fail validation");
    }

    [Fact]
    public void ValidationParameters_EnforceLifetimeValidation()
    {
        var (_, pub) = GenerateKeyPair();
        var validator = KubeconfigJwtService.ForValidation(pub);

        var params_ = validator.GetValidationParameters();
        params_.ValidateLifetime.Should().BeTrue(
            "kubeconfig JWTs must enforce expiry");
        params_.ClockSkew.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Issue_ThrowsOnValidationOnlyInstance()
    {
        var (_, pub) = GenerateKeyPair();
        var validator = KubeconfigJwtService.ForValidation(pub);

        var act = () => validator.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No signing key*");
    }

    [Fact]
    public void ForSigning_CanAlsoValidate()
    {
        var (priv, _) = GenerateKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);

        var token = signer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1));

        // ForSigning can also validate (ControlPlane needs both)
        var result = signer.Validate(token);
        result.Should().NotBeNull();
    }

    [Fact]
    public void Issue_SetsCorrectIssuerAndAudience()
    {
        var (priv, _) = GenerateKeyPair();
        var signer = KubeconfigJwtService.ForSigning(priv);

        var token = signer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.Should().Be(KubeconfigJwtService.IssuerName);
        jwt.Audiences.Should().Contain(KubeconfigJwtService.AudienceName);
    }

    [Fact]
    public void Validate_RejectsOidcToken()
    {
        var (_, pub) = GenerateKeyPair();
        var validator = KubeconfigJwtService.ForValidation(pub);

        // A random string that looks like a JWT but isn't signed with our key
        var fakeToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.fake-signature";

        var result = validator.Validate(fakeToken);
        result.Should().BeNull("OIDC/fake token should not pass kubeconfig validation");
    }
}
