using System.Security.Cryptography;
using System.Text;

namespace Clustral.ControlPlane.Features.Shared;

/// <summary>
/// Shared service for generating and hashing bearer tokens.
/// Extracted from ClustersController and AuthController for reuse
/// across feature slices.
/// </summary>
public sealed class TokenHashingService
{
    /// <summary>
    /// Generates a cryptographically random 32-byte token, base64url-encoded.
    /// </summary>
    public string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Returns the SHA-256 hex digest (lowercase) of the raw token.
    /// Must stay consistent with <c>AuthController.HashToken</c> and
    /// <c>AuthServiceImpl.HashToken</c>.
    /// </summary>
    public string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
