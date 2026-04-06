using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clustral.ControlPlane.Tests;

/// <summary>
/// Test authentication handler that always succeeds and injects configurable
/// claims. Used by <see cref="ClustralWebApplicationFactory"/> to bypass
/// real Keycloak JWT validation in integration tests.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string DefaultSub = "test-subject-001";
    public const string DefaultEmail = "test@clustral.local";
    public const string DefaultName = "Test User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Allow unauthenticated requests (no Authorization header) to pass through
        // so [AllowAnonymous] endpoints work naturally.
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());

        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("Missing Bearer token."));

        // Extract sub/email from a custom header if provided, otherwise use defaults.
        var sub   = Request.Headers["X-Test-Sub"].FirstOrDefault()   ?? DefaultSub;
        var email = Request.Headers["X-Test-Email"].FirstOrDefault() ?? DefaultEmail;
        var name  = Request.Headers["X-Test-Name"].FirstOrDefault()  ?? DefaultName;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim("sub", sub),
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email),
            new Claim(ClaimTypes.Name, name),
            new Claim("name", name),
            new Claim("preferred_username", email),
        };

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
