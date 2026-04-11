using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Clustral.Sdk.Auth;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ApiGateway.Tests;

public sealed class InternalJwtTests(ITestOutputHelper output)
{
    [Fact]
    public void Issue_CreatesValidJwt_WithClaims()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPem = key.ExportECPrivateKeyPem();
        var publicKeyPem = key.ExportSubjectPublicKeyInfoPem();

        var signer = InternalJwtService.ForSigning(privateKeyPem);
        var validator = InternalJwtService.ForValidation(publicKeyPem);

        var claims = new[]
        {
            new Claim("sub", "user-123"),
            new Claim("email", "user@example.com"),
            new Claim("name", "Test User"),
            new Claim("preferred_username", "testuser"),
        };

        var token = signer.Issue(claims);
        output.WriteLine($"Token: {token[..50]}...");

        token.Should().NotBeNullOrEmpty();

        // Validate with public key
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, validator.GetValidationParameters(), out _);

        principal.FindFirst("sub")?.Value.Should().Be("user-123");
        principal.FindFirst("email")?.Value.Should().Be("user@example.com");
    }

    [Fact]
    public void Issue_ExcludesNonStandardClaims()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = InternalJwtService.ForSigning(key.ExportECPrivateKeyPem());

        var claims = new[]
        {
            new Claim("sub", "user-123"),
            new Claim("email", "user@example.com"),
            new Claim("custom_claim", "should-not-appear"),
            new Claim("internal_role", "admin"),
        };

        var token = signer.Issue(claims);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == "sub");
        jwt.Claims.Should().Contain(c => c.Type == "email");
        jwt.Claims.Should().NotContain(c => c.Type == "custom_claim");
        jwt.Claims.Should().NotContain(c => c.Type == "internal_role");
    }

    [Fact]
    public void Issue_SetsShortExpiry()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = InternalJwtService.ForSigning(key.ExportECPrivateKeyPem());

        var token = signer.Issue([new Claim("sub", "user-123")]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        var ttl = jwt.ValidTo - jwt.ValidFrom;
        output.WriteLine($"TTL: {ttl.TotalSeconds}s");

        ttl.TotalSeconds.Should().BeLessThanOrEqualTo(60,
            "internal JWT should have short TTL (default 30s)");
    }

    [Fact]
    public void Validate_RejectsTokenSignedWithDifferentKey()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var differentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var signer = InternalJwtService.ForSigning(signingKey.ExportECPrivateKeyPem());
        var validator = InternalJwtService.ForValidation(differentKey.ExportSubjectPublicKeyInfoPem());

        var token = signer.Issue([new Claim("sub", "user-123")]);
        var handler = new JwtSecurityTokenHandler();

        var act = () => handler.ValidateToken(token, validator.GetValidationParameters(), out _);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ForValidation_CannotIssueTokens()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var validator = InternalJwtService.ForValidation(key.ExportSubjectPublicKeyInfoPem());

        var act = () => validator.Issue([new Claim("sub", "user-123")]);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No signing key*");
    }

    [Fact]
    public void ForSigning_CannotGetValidationParameters()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = InternalJwtService.ForSigning(key.ExportECPrivateKeyPem());

        var act = () => signer.GetValidationParameters();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No validation key*");
    }
}
