using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Auth;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Auth;

/// <summary>
/// Tests for <see cref="SessionHelper.EnsureValidTokenAsync"/> — the
/// auto-login prompt that fires when the JWT is missing or expired.
///
/// These tests exercise the helper's token-reading and expiry-checking
/// logic. The OIDC re-login flow itself is NOT tested here (it requires
/// a browser + callback server) — only the decision paths.
/// </summary>
public sealed class SessionHelperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tokenPath;
    private readonly string? _origHome;

    public SessionHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-session-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, ".clustral"));
        _tokenPath = Path.Combine(_tempDir, ".clustral", "token");

        _origHome = Environment.GetEnvironmentVariable("HOME");
        Environment.SetEnvironmentVariable("HOME", _tempDir);
        Environment.SetEnvironmentVariable("USERPROFILE", _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _origHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _origHome);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static CliConfig TestConfig() => new()
    {
        ControlPlaneUrl = "http://localhost:5100",
        OidcAuthority = "http://localhost:8080/realms/clustral",
        OidcClientId = "clustral-cli",
        OidcScopes = "openid email profile",
        CallbackPort = 7777,
    };

    /// <summary>
    /// Creates a minimal JWT with the given expiry (Unix timestamp in the
    /// "exp" claim). Not cryptographically valid — just enough for
    /// DecodeJwtExpiry to parse.
    /// </summary>
    private static string CreateFakeJwt(DateTimeOffset expiresAt)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"none"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $$"""{"sub":"test","email":"test@example.com","exp":{{expiresAt.ToUnixTimeSeconds()}}}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payload}.";
    }

    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidToken_ReturnsImmediately()
    {
        var jwt = CreateFakeJwt(DateTimeOffset.UtcNow.AddHours(8));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var result = await SessionHelper.EnsureValidTokenAsync(
            TestConfig(), insecure: false, CancellationToken.None);

        result.Should().Be(jwt);
    }

    [Fact]
    public async Task MissingToken_ReturnsNull_WhenNonInteractive()
    {
        // In test runners, Console.IsOutputRedirected is true → non-interactive.
        // So the helper should return null without prompting.
        var result = await SessionHelper.EnsureValidTokenAsync(
            TestConfig(), insecure: false, CancellationToken.None);

        result.Should().BeNull("non-interactive session should not prompt");
    }

    [Fact]
    public async Task ExpiredToken_ReturnsNull_WhenNonInteractive()
    {
        var jwt = CreateFakeJwt(DateTimeOffset.UtcNow.AddHours(-1));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var result = await SessionHelper.EnsureValidTokenAsync(
            TestConfig(), insecure: false, CancellationToken.None);

        result.Should().BeNull("expired token in non-interactive session should return null");
    }

    [Fact]
    public async Task NoOidcConfig_ReturnsNull()
    {
        // Even with a missing token, if there's no OIDC config we can't re-login.
        var config = new CliConfig { ControlPlaneUrl = "http://localhost" };
        // OidcAuthority is empty

        var result = await SessionHelper.EnsureValidTokenAsync(
            config, insecure: false, CancellationToken.None);

        result.Should().BeNull("cannot auto-login without OIDC configuration");
    }

    [Fact]
    public async Task NoControlPlaneUrl_ReturnsNull()
    {
        var config = new CliConfig { OidcAuthority = "http://keycloak" };
        // ControlPlaneUrl is empty

        var result = await SessionHelper.EnsureValidTokenAsync(
            config, insecure: false, CancellationToken.None);

        result.Should().BeNull("cannot auto-login without ControlPlane URL");
    }

    [Fact]
    public async Task TokenWithNoExpClaim_ReturnsNull_WhenNonInteractive()
    {
        // Token without "exp" claim — can't determine if valid.
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"none"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"sub":"test"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = $"{header}.{payload}.";
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var result = await SessionHelper.EnsureValidTokenAsync(
            TestConfig(), insecure: false, CancellationToken.None);

        // No exp claim → expiry is null → token treated as invalid → prompt
        // → non-interactive → return null
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidToken_FarFuture_ReturnsToken()
    {
        var jwt = CreateFakeJwt(DateTimeOffset.UtcNow.AddDays(30));
        await File.WriteAllTextAsync(_tokenPath, jwt);

        var result = await SessionHelper.EnsureValidTokenAsync(
            TestConfig(), insecure: false, CancellationToken.None);

        result.Should().Be(jwt);
    }
}
