using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clustral.Sdk.Auth;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Auth;

/// <summary>
/// Verifies that <see cref="Es256JwtService"/> accepts X.509 certificate PEM
/// (as output by cert-manager) alongside raw EC public key PEM. This is the
/// compatibility layer that makes cert-manager integration work without
/// template-side key extraction.
/// </summary>
public sealed class Es256JwtServiceCertTests(ITestOutputHelper output)
{
    private static (string privateKeyPem, string publicKeyPem, string certPem) GenerateKeyPairWithCert()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPem = key.ExportECPrivateKeyPem();
        var publicKeyPem = key.ExportSubjectPublicKeyInfoPem();

        // Self-signed X.509 cert wrapping the same public key — mimics cert-manager output.
        var req = new CertificateRequest("CN=clustral-jwt", key, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var certPem = cert.ExportCertificatePem();

        return (privateKeyPem, publicKeyPem, certPem);
    }

    [Fact]
    public void ForValidation_AcceptsRawPublicKeyPem()
    {
        var (priv, pub, _) = GenerateKeyPairWithCert();
        var signer = Es256JwtService.ForSigning(priv, "iss", "aud");
        var validator = Es256JwtService.ForValidation(pub, "iss", "aud");

        var token = signer.Issue([], DateTimeOffset.UtcNow.AddMinutes(5));
        var result = validator.Validate(token);

        result.Should().NotBeNull("raw EC public key PEM must work (backward compat)");
        output.WriteLine("Raw PEM validation: OK");
    }

    [Fact]
    public void ForValidation_AcceptsCertificatePem()
    {
        var (priv, _, certPem) = GenerateKeyPairWithCert();
        var signer = Es256JwtService.ForSigning(priv, "iss", "aud");

        // Validate using the X.509 certificate PEM (cert-manager output)
        var validator = Es256JwtService.ForValidation(certPem, "iss", "aud");

        var token = signer.Issue([], DateTimeOffset.UtcNow.AddMinutes(5));
        var result = validator.Validate(token);

        result.Should().NotBeNull("X.509 certificate PEM must be accepted (cert-manager compat)");
        output.WriteLine("Certificate PEM validation: OK");
    }

    [Fact]
    public void ForValidation_CertificatePem_RejectsDifferentKey()
    {
        var (priv, _, _) = GenerateKeyPairWithCert();
        var (_, _, otherCertPem) = GenerateKeyPairWithCert();

        var signer = Es256JwtService.ForSigning(priv, "iss", "aud");
        var validator = Es256JwtService.ForValidation(otherCertPem, "iss", "aud");

        var token = signer.Issue([], DateTimeOffset.UtcNow.AddMinutes(5));
        var result = validator.Validate(token);

        result.Should().BeNull("token signed with different key should fail cert-based validation");
    }

    [Fact]
    public void ForValidation_CertificatePem_RawPem_SameKey_BothWork()
    {
        var (priv, pub, certPem) = GenerateKeyPairWithCert();
        var signer = Es256JwtService.ForSigning(priv, "iss", "aud");
        var token = signer.Issue([], DateTimeOffset.UtcNow.AddMinutes(5));

        var rawValidator = Es256JwtService.ForValidation(pub, "iss", "aud");
        var certValidator = Es256JwtService.ForValidation(certPem, "iss", "aud");

        rawValidator.Validate(token).Should().NotBeNull("raw PEM path");
        certValidator.Validate(token).Should().NotBeNull("cert PEM path");
        output.WriteLine("Both validation paths accept the same token: OK");
    }

    [Fact]
    public void ForValidation_RsaCertificate_ThrowsOnNonEcdsaKey()
    {
        // cert-manager could theoretically issue an RSA cert if misconfigured.
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=wrong-alg", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var rsaCertPem = cert.ExportCertificatePem();

        var act = () => Es256JwtService.ForValidation(rsaCertPem, "iss", "aud");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ECDSA*");
    }

    [Fact]
    public void InternalJwtService_WorksWithCertificatePem()
    {
        var (priv, _, certPem) = GenerateKeyPairWithCert();

        var signer = InternalJwtService.ForSigning(priv);
        var validator = InternalJwtService.ForValidation(certPem);

        var claims = new[]
        {
            new System.Security.Claims.Claim("sub", "user-123"),
            new System.Security.Claims.Claim("email", "alice@example.com"),
        };
        var token = signer.Issue(claims);
        output.WriteLine($"Internal JWT: {token[..60]}...");

        var result = validator.GetValidationParameters();
        result.Should().NotBeNull();
        result.IssuerSigningKey.Should().NotBeNull();
    }

    [Fact]
    public void KubeconfigJwtService_WorksWithCertificatePem()
    {
        var (priv, _, certPem) = GenerateKeyPairWithCert();

        var signer = KubeconfigJwtService.ForSigning(priv);
        var validator = KubeconfigJwtService.ForValidation(certPem);

        var token = signer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1));
        output.WriteLine($"Kubeconfig JWT: {token[..60]}...");

        var result = validator.Validate(token);
        result.Should().NotBeNull("kubeconfig JWT signed with raw key must validate via cert PEM");
    }
}
