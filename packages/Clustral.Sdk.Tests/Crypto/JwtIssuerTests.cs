using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clustral.Sdk.Crypto;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Crypto;

public sealed class JwtIssuerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly CertificateAuthority _ca;
    private readonly JwtIssuer _issuer;
    private readonly CertificateAuthorityOptions _options;

    public JwtIssuerTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-jwt-test-{Guid.NewGuid():N}");
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
        _issuer = new JwtIssuer(_ca, _options);
    }

    [Fact]
    public void IssueToken_ReturnsValidJwt()
    {
        var agentId = Guid.NewGuid().ToString();
        var clusterId = Guid.NewGuid().ToString();
        var allowedRpcs = new List<string> { "TunnelService/OpenTunnel", "ClusterService/UpdateStatus" };

        var jwt = _issuer.IssueToken(agentId, "test-org", clusterId, allowedRpcs, tokenVersion: 1);

        jwt.Should().NotBeNullOrWhiteSpace();
        jwt.Split('.').Should().HaveCount(3); // header.payload.signature

        _output.WriteLine($"JWT length: {jwt.Length}");
        _output.WriteLine(jwt);
    }

    [Fact]
    public void ValidateToken_ValidJwt_ReturnsPrincipal()
    {
        var agentId = Guid.NewGuid().ToString();
        var clusterId = Guid.NewGuid().ToString();
        var allowedRpcs = new List<string> { "TunnelService/OpenTunnel" };

        var jwt = _issuer.IssueToken(agentId, "test-org", clusterId, allowedRpcs, tokenVersion: 5);
        var principal = _issuer.ValidateToken(jwt);

        JwtIssuer.GetAgentId(principal).Should().Be(agentId);
        JwtIssuer.GetClusterId(principal).Should().Be(clusterId);
        JwtIssuer.GetTokenVersion(principal).Should().Be(5);
        JwtIssuer.GetAllowedRpcs(principal).Should().BeEquivalentTo(allowedRpcs);
    }

    [Fact]
    public void ValidateToken_WrongKey_Throws()
    {
        var jwt = _issuer.IssueToken("agent-1", "org-1", "cluster-1", ["rpc1"], tokenVersion: 1);

        // Create a different CA with a different key
        var otherDir = Path.Combine(_tempDir, "other");
        Directory.CreateDirectory(otherDir);
        var otherCertPath = Path.Combine(otherDir, "ca.crt");
        var otherKeyPath = Path.Combine(otherDir, "ca.key");
        GenerateTestCA(otherCertPath, otherKeyPath);

        var otherCa = new CertificateAuthority(otherCertPath, otherKeyPath, _options);
        var otherIssuer = new JwtIssuer(otherCa, _options);

        var act = () => otherIssuer.ValidateToken(jwt);
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void ValidateToken_TamperedJwt_Throws()
    {
        var jwt = _issuer.IssueToken("agent-1", "org-1", "cluster-1", ["rpc1"], tokenVersion: 1);

        // Tamper with the payload by changing a character in the middle section
        var parts = jwt.Split('.');
        var tampered = parts[0] + "." + parts[1][..^1] + "X" + "." + parts[2];

        var act = () => _issuer.ValidateToken(tampered);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void GetTokenExpiry_ReturnsCorrectDate()
    {
        var expiry = _issuer.GetTokenExpiry();

        expiry.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(29));
        expiry.Should().BeBefore(DateTimeOffset.UtcNow.AddDays(31));
    }

    [Fact]
    public void GetAllowedRpcs_EmptyClaim_ReturnsEmptyList()
    {
        var jwt = _issuer.IssueToken("agent-1", "org-1", "cluster-1", new List<string>(), tokenVersion: 1);
        var principal = _issuer.ValidateToken(jwt);

        JwtIssuer.GetAllowedRpcs(principal).Should().BeEmpty();
    }

    [Fact]
    public void GetTokenVersion_MissingClaim_ReturnsZero()
    {
        // Use a hand-crafted principal without token_version
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity());

        JwtIssuer.GetTokenVersion(principal).Should().Be(0);
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
