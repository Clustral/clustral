using System.Security.Cryptography;
using System.Text;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Infrastructure.Auth;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

public class AuthModelsTests(ITestOutputHelper output)
{
    [Fact]
    public void IssueKubeconfigCredentialRequest_RequiredClusterId()
    {
        var request = new IssueKubeconfigCredentialRequest(ClusterId: Guid.NewGuid());

        output.WriteLine($"ClusterId:    {request.ClusterId}");
        output.WriteLine($"RequestedTtl: {request.RequestedTtl ?? "null (use default)"}");

        Assert.NotEqual(Guid.Empty, request.ClusterId);
        Assert.Null(request.RequestedTtl);
    }

    [Fact]
    public void IssueKubeconfigCredentialRequest_WithTtl()
    {
        var request = new IssueKubeconfigCredentialRequest(
            ClusterId: Guid.NewGuid(),
            RequestedTtl: "PT4H");

        output.WriteLine($"RequestedTtl: {request.RequestedTtl}");

        Assert.Equal("PT4H", request.RequestedTtl);
    }

    [Fact]
    public void IssueKubeconfigCredentialResponse_AllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new IssueKubeconfigCredentialResponse(
            CredentialId: Guid.NewGuid(),
            Token: "raw-bearer-token",
            IssuedAt: now,
            ExpiresAt: now.AddHours(8),
            Subject: "keycloak-sub-123",
            DisplayName: "Alice");

        output.WriteLine($"=== IssueKubeconfigCredentialResponse ===");
        output.WriteLine($"  CredentialId: {response.CredentialId}");
        output.WriteLine($"  Token:        {response.Token[..5]}...");
        output.WriteLine($"  IssuedAt:     {response.IssuedAt}");
        output.WriteLine($"  ExpiresAt:    {response.ExpiresAt}");
        output.WriteLine($"  Subject:      {response.Subject}");
        output.WriteLine($"  DisplayName:  {response.DisplayName}");

        Assert.Equal("raw-bearer-token", response.Token);
        Assert.Equal(now.AddHours(8), response.ExpiresAt);
    }

    [Fact]
    public void RevokeCredentialResponse_AllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new RevokeCredentialResponse(Revoked: true, RevokedAt: now);

        output.WriteLine($"Revoked:   {response.Revoked}");
        output.WriteLine($"RevokedAt: {response.RevokedAt}");

        Assert.True(response.Revoked);
    }

    // ── Token hashing consistency ───────────────────────────────────────────

    [Fact]
    public void TokenHash_IsSha256Hex()
    {
        var rawToken = "test-token-abc123";
        var hash = HashToken(rawToken);

        output.WriteLine($"Raw:  {rawToken}");
        output.WriteLine($"Hash: {hash}");
        output.WriteLine($"Length: {hash.Length} chars (64 expected for SHA-256 hex)");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void TokenHash_IsDeterministic()
    {
        var rawToken = "deterministic-test";
        var hash1 = HashToken(rawToken);
        var hash2 = HashToken(rawToken);

        output.WriteLine($"Hash1: {hash1}");
        output.WriteLine($"Hash2: {hash2}");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void TokenHash_DifferentTokens_DifferentHashes()
    {
        var hash1 = HashToken("token-a");
        var hash2 = HashToken("token-b");

        output.WriteLine($"token-a => {hash1}");
        output.WriteLine($"token-b => {hash2}");

        Assert.NotEqual(hash1, hash2);
    }

    // ── OidcOptions defaults ────────────────────────────────────────────

    [Fact]
    public void OidcOptions_Defaults()
    {
        var opts = new OidcOptions();

        output.WriteLine($"=== OidcOptions Defaults ===");
        output.WriteLine($"  Authority:          \"{opts.Authority}\"");
        output.WriteLine($"  ClientId:           \"{opts.ClientId}\"");
        output.WriteLine($"  Audience:           \"{opts.Audience}\"");
        output.WriteLine($"  RequireHttpsMeta:   {opts.RequireHttpsMetadata}");
        output.WriteLine($"  MetadataAddress:    \"{opts.MetadataAddress}\"");
        output.WriteLine($"  DefaultCredTtl:     {opts.DefaultKubeconfigCredentialTtl}");
        output.WriteLine($"  MaxCredTtl:         {opts.MaxKubeconfigCredentialTtl}");

        Assert.Equal(string.Empty, opts.Authority);
        Assert.Equal(string.Empty, opts.ClientId);
        Assert.True(opts.RequireHttpsMetadata);
        Assert.Equal(TimeSpan.FromHours(8), opts.DefaultKubeconfigCredentialTtl);
        Assert.Equal(TimeSpan.FromHours(8), opts.MaxKubeconfigCredentialTtl);
    }

    [Fact]
    public void CredentialTtl_CappedAtMax()
    {
        var opts = new OidcOptions
        {
            DefaultKubeconfigCredentialTtl = TimeSpan.FromHours(8),
            MaxKubeconfigCredentialTtl = TimeSpan.FromHours(8),
        };

        var requested = TimeSpan.FromHours(24);
        var capped = requested < opts.MaxKubeconfigCredentialTtl
            ? requested
            : opts.MaxKubeconfigCredentialTtl;

        output.WriteLine($"Requested: {requested}");
        output.WriteLine($"Max:       {opts.MaxKubeconfigCredentialTtl}");
        output.WriteLine($"Result:    {capped}");

        Assert.Equal(TimeSpan.FromHours(8), capped);
    }

    [Fact]
    public void CredentialTtl_UnderMax_UsesRequested()
    {
        var opts = new OidcOptions
        {
            MaxKubeconfigCredentialTtl = TimeSpan.FromHours(8),
        };

        var requested = TimeSpan.FromHours(4);
        var capped = requested < opts.MaxKubeconfigCredentialTtl
            ? requested
            : opts.MaxKubeconfigCredentialTtl;

        output.WriteLine($"Requested: {requested} (under max) => {capped}");

        Assert.Equal(TimeSpan.FromHours(4), capped);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    /// <summary>Same logic as AuthController.HashToken</summary>
    private static string HashToken(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
