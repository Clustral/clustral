using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clustral.Sdk.Crypto;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Crypto;

public sealed class CertificateAuthorityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly CertificateAuthority _ca;
    private readonly CertificateAuthorityOptions _options;

    public CertificateAuthorityTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-ca-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _options = new CertificateAuthorityOptions
        {
            CaCertPath = Path.Combine(_tempDir, "ca.crt"),
            CaKeyPath = Path.Combine(_tempDir, "ca.key"),
            ClientCertValidityDays = 395,
            JwtValidityDays = 30,
        };

        GenerateTestCA(_options.CaCertPath, _options.CaKeyPath);
        _ca = new CertificateAuthority(_options.CaCertPath, _options.CaKeyPath, _options);
    }

    [Fact]
    public void GetCaCertificatePem_ReturnsPem()
    {
        var pem = _ca.GetCaCertificatePem();

        pem.Should().Contain("-----BEGIN CERTIFICATE-----");
        pem.Should().Contain("-----END CERTIFICATE-----");
        _output.WriteLine(pem);
    }

    [Fact]
    public void IssueCertificate_ReturnsValidCertAndKey()
    {
        var agentId = Guid.NewGuid().ToString();
        var clusterId = Guid.NewGuid().ToString();
        var orgId = "test-org";

        var (certPem, keyPem) = _ca.IssueCertificate(agentId, clusterId, orgId);

        certPem.Should().Contain("-----BEGIN CERTIFICATE-----");
        keyPem.Should().Contain("-----BEGIN PRIVATE KEY-----");

        var cert = X509Certificate2.CreateFromPem(certPem, keyPem);
        cert.Subject.Should().Contain($"CN={agentId}");
        cert.Subject.Should().Contain($"O={orgId}");
        cert.NotAfter.Should().BeAfter(DateTime.UtcNow.AddDays(394));
        cert.NotAfter.Should().BeBefore(DateTime.UtcNow.AddDays(396));

        _output.WriteLine($"Subject: {cert.Subject}");
        _output.WriteLine($"NotAfter: {cert.NotAfter}");
    }

    [Fact]
    public void IssueCertificate_HasCorrectKeyUsage()
    {
        var (certPem, keyPem) = _ca.IssueCertificate("agent-1", "cluster-1", "org-1");
        var cert = X509Certificate2.CreateFromPem(certPem, keyPem);

        var keyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        keyUsage.Should().NotBeNull();
        keyUsage!.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.DigitalSignature);
        keyUsage.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.KeyEncipherment);

        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        eku.Should().NotBeNull();
        eku!.EnhancedKeyUsages.Cast<Oid>().Should().Contain(o => o.Value == "1.3.6.1.5.5.7.3.2");
    }

    [Fact]
    public void ValidateChain_ValidCert_ReturnsTrue()
    {
        var (certPem, keyPem) = _ca.IssueCertificate("agent-1", "cluster-1", "org-1");
        var cert = X509Certificate2.CreateFromPem(certPem);

        _ca.ValidateChain(cert).Should().BeTrue();
    }

    [Fact]
    public void ValidateChain_UntrustedCert_ReturnsFalse()
    {
        // Generate a self-signed cert NOT issued by the CA
        using var untrustedKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=untrusted", untrustedKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var untrustedCert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

        _ca.ValidateChain(untrustedCert).Should().BeFalse();
    }

    [Fact]
    public void Fingerprint_ReturnsHexString()
    {
        var (certPem, _) = _ca.IssueCertificate("agent-1", "cluster-1", "org-1");
        var cert = X509Certificate2.CreateFromPem(certPem);

        var fingerprint = CertificateAuthority.Fingerprint(cert);

        fingerprint.Should().HaveLength(64); // SHA-256 = 32 bytes = 64 hex chars
        fingerprint.Should().MatchRegex("^[0-9a-f]+$");

        _output.WriteLine($"Fingerprint: {fingerprint}");
    }

    [Fact]
    public void IssueCertificate_DifferentAgents_DifferentSerials()
    {
        var (cert1Pem, _) = _ca.IssueCertificate("agent-1", "cluster-1", "org-1");
        var (cert2Pem, _) = _ca.IssueCertificate("agent-2", "cluster-1", "org-1");

        var cert1 = X509Certificate2.CreateFromPem(cert1Pem);
        var cert2 = X509Certificate2.CreateFromPem(cert2Pem);

        cert1.SerialNumber.Should().NotBe(cert2.SerialNumber);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }

    private static void GenerateTestCA(string certPath, string keyPath)
    {
        using var caKey = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Clustral Test CA",
            caKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        using var caCert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(10));

        File.WriteAllText(certPath, caCert.ExportCertificatePem());
        File.WriteAllText(keyPath, caKey.ExportPkcs8PrivateKeyPem());
    }
}
