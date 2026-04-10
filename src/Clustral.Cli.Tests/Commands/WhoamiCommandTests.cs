using System.Text;
using Clustral.Cli.Commands;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <see cref="WhoamiCommand"/> — JWT decoding and one-liner output.
/// </summary>
public sealed class WhoamiCommandTests(ITestOutputHelper output)
{
    private static string CreateFakeJwt(string? email, DateTimeOffset? expiresAt, string? sub = null)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"none"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var claims = new StringBuilder("{");
        if (sub is not null) claims.Append($"\"sub\":\"{sub}\"");
        if (email is not null)
        {
            if (claims.Length > 1) claims.Append(',');
            claims.Append($"\"email\":\"{email}\"");
        }
        if (expiresAt.HasValue)
        {
            if (claims.Length > 1) claims.Append(',');
            claims.Append($"\"exp\":{expiresAt.Value.ToUnixTimeSeconds()}");
        }
        claims.Append('}');

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(claims.ToString()))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payload}.";
    }

    // ── DecodeEmailAndExpiry ─────────────────────────────────────────────────

    [Fact]
    public void Decode_EmailAndExpiry_FromValidJwt()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(8);
        var jwt = CreateFakeJwt("alice@example.com", expiry);

        var (email, exp) = WhoamiCommand.DecodeEmailAndExpiry(jwt);

        output.WriteLine($"Email: {email}, Expires: {exp}");
        email.Should().Be("alice@example.com");
        exp.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Decode_FallsBackToSub_WhenNoEmail()
    {
        var jwt = CreateFakeJwt(null, DateTimeOffset.UtcNow.AddHours(1), sub: "user-123");

        var (email, _) = WhoamiCommand.DecodeEmailAndExpiry(jwt);

        email.Should().Be("user-123");
    }

    [Fact]
    public void Decode_ExpiredToken_ReturnsExpiry()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(-2);
        var jwt = CreateFakeJwt("bob@example.com", expiry);

        var (email, exp) = WhoamiCommand.DecodeEmailAndExpiry(jwt);

        email.Should().Be("bob@example.com");
        exp.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Decode_NoExpClaim_ReturnsNullExpiry()
    {
        var jwt = CreateFakeJwt("alice@example.com", null);

        var (email, exp) = WhoamiCommand.DecodeEmailAndExpiry(jwt);

        email.Should().Be("alice@example.com");
        exp.Should().BeNull();
    }

    [Fact]
    public void Decode_MalformedToken_ReturnsNulls()
    {
        var (email, exp) = WhoamiCommand.DecodeEmailAndExpiry("not-a-jwt");

        email.Should().BeNull();
        exp.Should().BeNull();
    }

    [Fact]
    public void Decode_EmptyPayload_ReturnsNulls()
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var (email, exp) = WhoamiCommand.DecodeEmailAndExpiry($"{header}.{payload}.");

        email.Should().BeNull();
        exp.Should().BeNull();
    }
}
