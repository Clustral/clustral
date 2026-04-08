using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Clustral.Sdk.Crypto;

/// <summary>
/// Internal Certificate Authority for Clustral. Loads a CA cert + private key
/// from PEM files and issues RSA client certificates for agent mTLS.
/// </summary>
public sealed class CertificateAuthority
{
    private readonly X509Certificate2 _caCert;
    private readonly RSA _caKey;
    private readonly CertificateAuthorityOptions _options;

    public CertificateAuthority(string caCertPath, string caKeyPath, CertificateAuthorityOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caCertPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(caKeyPath);

        _options = options ?? throw new ArgumentNullException(nameof(options));

        var certPem = File.ReadAllText(caCertPath);
        var keyPem = File.ReadAllText(caKeyPath);

        _caCert = X509Certificate2.CreateFromPem(certPem, keyPem);
        _caKey = _caCert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("CA certificate does not contain an RSA private key.");
    }

    /// <summary>
    /// Issues a client certificate signed by this CA.
    /// </summary>
    /// <param name="agentId">Agent identifier — used as the certificate CN.</param>
    /// <param name="clusterId">Cluster identifier — included in Subject Alternative Name.</param>
    /// <param name="orgId">Organization identifier — included in the Subject O field.</param>
    /// <returns>PEM-encoded client certificate and private key.</returns>
    public (string CertPem, string KeyPem) IssueCertificate(string agentId, string clusterId, string orgId)
    {
        using var clientKey = RSA.Create(2048);

        var subject = new X500DistinguishedName($"CN={agentId}, O={orgId}");
        var request = new CertificateRequest(subject, clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Key Usage: Digital Signature + Key Encipherment
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        // Extended Key Usage: Client Authentication
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.2") }, // id-kp-clientAuth
                critical: false));

        // Subject Alternative Name: URI with clusterId
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri($"urn:clustral:cluster:{clusterId}"));
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Authority Key Identifier (only if CA has Subject Key Identifier)
        var ski = _caCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault();
        if (ski is not null)
        {
            request.CertificateExtensions.Add(
                X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                    _caCert, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        }

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddDays(_options.ClientCertValidityDays);
        var serialNumber = GenerateSerialNumber();

        using var clientCert = request.Create(_caCert, notBefore, notAfter, serialNumber);

        var certPem = ExportCertificatePem(clientCert);
        var keyPem = ExportPrivateKeyPem(clientKey);

        return (certPem, keyPem);
    }

    /// <summary>Returns the CA certificate in PEM format.</summary>
    public string GetCaCertificatePem() => ExportCertificatePem(_caCert);

    /// <summary>Returns the CA's RSA private key for JWT signing.</summary>
    public RSA GetSigningKey() => _caKey;

    /// <summary>
    /// Validates that the given client certificate was signed by this CA.
    /// </summary>
    public bool ValidateChain(X509Certificate2 clientCert)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(_caCert);

        return chain.Build(clientCert);
    }

    /// <summary>
    /// Computes the SHA-256 fingerprint of a certificate (hex, lowercase).
    /// </summary>
    public static string Fingerprint(X509Certificate2 cert)
    {
        var hash = cert.GetCertHash(HashAlgorithmName.SHA256);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] GenerateSerialNumber()
    {
        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F; // ensure positive
        return serial;
    }

    private static string ExportCertificatePem(X509Certificate2 cert)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN CERTIFICATE-----");
        sb.AppendLine(Convert.ToBase64String(cert.RawData, Base64FormattingOptions.InsertLineBreaks));
        sb.AppendLine("-----END CERTIFICATE-----");
        return sb.ToString();
    }

    private static string ExportPrivateKeyPem(RSA key)
    {
        var pkcs8 = key.ExportPkcs8PrivateKey();
        var sb = new StringBuilder();
        sb.AppendLine("-----BEGIN PRIVATE KEY-----");
        sb.AppendLine(Convert.ToBase64String(pkcs8, Base64FormattingOptions.InsertLineBreaks));
        sb.AppendLine("-----END PRIVATE KEY-----");
        return sb.ToString();
    }
}
