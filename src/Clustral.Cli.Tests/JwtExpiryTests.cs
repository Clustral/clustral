using System.Text;
using System.Text.Json;
using Clustral.Cli.Commands;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class JwtExpiryTests(ITestOutputHelper output)
{
    private static string BuildFakeJwt(object payload)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
        var body = Base64UrlEncode(JsonSerializer.Serialize(payload));
        var sig = "fake-signature";
        return $"{header}.{body}.{sig}";
    }

    private static string Base64UrlEncode(string input) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    [Fact]
    public void DecodeJwtExpiry_ValidToken_ReturnsExpiry()
    {
        var exp = DateTimeOffset.UtcNow.AddHours(8);
        var token = BuildFakeJwt(new { exp = exp.ToUnixTimeSeconds() });

        output.WriteLine($"Token:    {token[..40]}...");
        output.WriteLine($"Expected: {exp:yyyy-MM-dd HH:mm:ss K}");

        var result = LoginCommand.DecodeJwtExpiry(token);

        output.WriteLine($"Decoded:  {result!.Value:yyyy-MM-dd HH:mm:ss K}");

        Assert.NotNull(result);
        Assert.Equal(exp.ToUnixTimeSeconds(), result!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void DecodeJwtExpiry_ExpiredToken_ReturnsExpiry()
    {
        var exp = DateTimeOffset.UtcNow.AddHours(-1);
        var token = BuildFakeJwt(new { exp = exp.ToUnixTimeSeconds() });

        var result = LoginCommand.DecodeJwtExpiry(token);

        output.WriteLine($"Token expired at: {result!.Value:yyyy-MM-dd HH:mm:ss K}");
        output.WriteLine($"Now:              {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss K}");
        output.WriteLine($"Expired:          yes");

        Assert.NotNull(result);
        Assert.True(result!.Value < DateTimeOffset.UtcNow);
    }

    [Fact]
    public void DecodeJwtExpiry_NoExpClaim_ReturnsNull()
    {
        var token = BuildFakeJwt(new { sub = "user123", email = "test@example.com" });

        output.WriteLine($"Token payload: {{sub: user123, email: test@example.com}}");
        output.WriteLine($"Result:        null (no exp claim)");

        var result = LoginCommand.DecodeJwtExpiry(token);
        Assert.Null(result);
    }

    [Fact]
    public void DecodeJwtExpiry_MalformedToken_ReturnsNull()
    {
        var cases = new[] { "not-a-jwt", "", "a.b" };
        foreach (var c in cases)
        {
            var result = LoginCommand.DecodeJwtExpiry(c);
            output.WriteLine($"DecodeJwtExpiry(\"{c}\") => {(result is null ? "null" : result.ToString())}");
            Assert.Null(result);
        }
    }

    [Fact]
    public void DecodeJwtExpiry_InvalidBase64Payload_ReturnsNull()
    {
        var token = "header.!!!invalid!!!.sig";

        output.WriteLine($"Token: \"{token}\"");

        var result = LoginCommand.DecodeJwtExpiry(token);

        output.WriteLine($"Result: {(result is null ? "null" : result.ToString())}");

        Assert.Null(result);
    }

    [Fact]
    public void DecodeJwtExpiry_TwoPartToken_DecodesPayload()
    {
        var exp = DateTimeOffset.UtcNow.AddHours(1);
        var header = Base64UrlEncode("""{"alg":"none"}""");
        var body = Base64UrlEncode(JsonSerializer.Serialize(new { exp = exp.ToUnixTimeSeconds() }));
        var token = $"{header}.{body}.";

        output.WriteLine($"Unsigned token (alg:none): {token[..40]}...");

        var result = LoginCommand.DecodeJwtExpiry(token);

        output.WriteLine($"Decoded expiry: {result!.Value:yyyy-MM-dd HH:mm:ss K}");

        Assert.NotNull(result);
    }
}
