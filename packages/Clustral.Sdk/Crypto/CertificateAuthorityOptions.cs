namespace Clustral.Sdk.Crypto;

/// <summary>
/// Configuration options for the Clustral internal Certificate Authority.
/// The CA signs agent client certificates for mTLS and provides the RSA key
/// used to sign agent JWTs.
/// </summary>
public sealed class CertificateAuthorityOptions
{
    public const string SectionName = "CertificateAuthority";

    /// <summary>Path to the PEM-encoded CA certificate file.</summary>
    public string CaCertPath { get; set; } = string.Empty;

    /// <summary>Path to the PEM-encoded CA private key file.</summary>
    public string CaKeyPath { get; set; } = string.Empty;

    /// <summary>Validity period for issued agent client certificates (default 395 days).</summary>
    public int ClientCertValidityDays { get; set; } = 395;

    /// <summary>Validity period for issued agent JWTs (default 30 days).</summary>
    public int JwtValidityDays { get; set; } = 30;
}
