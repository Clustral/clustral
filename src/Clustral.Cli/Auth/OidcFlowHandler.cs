using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Clustral.Cli.Config;
using Spectre.Console;

namespace Clustral.Cli.Auth;

/// <summary>
/// Orchestrates the OAuth2 Authorization Code + PKCE flow:
/// <list type="number">
///   <item>Generates a PKCE <c>code_verifier</c> and <c>code_challenge</c>.</item>
///   <item>Opens the system browser to the Keycloak authorization endpoint.</item>
///   <item>Waits for the redirect to <c>http://127.0.0.1:{port}/callback</c>.</item>
///   <item>Exchanges the authorization code for an access token.</item>
/// </list>
/// All operations are AOT-safe (no reflection, no <c>dynamic</c>).
/// </summary>
internal sealed class OidcFlowHandler
{
    private readonly string     _authority;
    private readonly string     _clientId;
    private readonly string     _scopes;
    private readonly int        _port;
    private readonly HttpClient _http;

    public OidcFlowHandler(
        string     authority,
        string     clientId,
        string     scopes,
        int        port,
        HttpClient http)
    {
        _authority = authority.TrimEnd('/');
        _clientId  = clientId;
        _scopes    = scopes;
        _port      = port;
        _http      = http;
    }

    /// <summary>
    /// Runs the full PKCE flow and returns the Keycloak access token.
    /// </summary>
    public async Task<string> LoginAsync(CancellationToken ct)
    {
        // ── PKCE ──────────────────────────────────────────────────────────────
        var verifier   = GenerateCodeVerifier();
        var challenge  = GenerateCodeChallenge(verifier);
        var state      = GenerateState();
        var redirectUri = $"http://127.0.0.1:{_port}/callback";

        // ── Build authorization URL ───────────────────────────────────────────
        var authUrl = BuildAuthorizationUrl(challenge, state, redirectUri);

        // ── Open browser ──────────────────────────────────────────────────────
        Spectre.Console.AnsiConsole.MarkupLine("  [cyan]●[/] Opening browser for SSO login...");

        OpenBrowser(authUrl);

        // ── Wait for callback ─────────────────────────────────────────────────
        string callbackQuery;
        using (var server = new OidcCallbackServer(_port))
        {
            Spectre.Console.AnsiConsole.MarkupLine("  [yellow]●[/] Waiting for authentication...");
            Spectre.Console.AnsiConsole.MarkupLine($"    [dim]If the browser did not open, visit:[/]");
            Spectre.Console.AnsiConsole.MarkupLine($"    [dim]{authUrl.EscapeMarkup()}[/]");
            Console.Error.WriteLine();

            callbackQuery = await server.WaitForCallbackAsync(ct);
        }

        // ── Parse callback query ──────────────────────────────────────────────
        var qs = HttpUtility.ParseQueryString(callbackQuery);

        if (qs["error"] is { } error)
            throw new InvalidOperationException(
                $"Keycloak returned an error: {error} — {qs["error_description"]}");

        var code         = qs["code"]  ?? throw new InvalidOperationException("No code in callback.");
        var returnedState = qs["state"] ?? throw new InvalidOperationException("No state in callback.");

        if (returnedState != state)
            throw new InvalidOperationException("State mismatch — possible CSRF attack.");

        // ── Token exchange ────────────────────────────────────────────────────
        var tokenResponse = await ExchangeCodeAsync(code, verifier, redirectUri, ct);
        return tokenResponse.AccessToken;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Token exchange
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<KeycloakTokenResponse> ExchangeCodeAsync(
        string code,
        string verifier,
        string redirectUri,
        CancellationToken ct)
    {
        var tokenEndpoint = $"{_authority}/protocol/openid-connect/token";

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = redirectUri,
            ["client_id"]     = _clientId,
            ["code_verifier"] = verifier,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = body,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Token exchange failed ({(int)response.StatusCode}): {detail}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, CliJsonContext.Default.KeycloakTokenResponse)
               ?? throw new InvalidOperationException("Empty token response from Keycloak.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PKCE helpers (RFC 7636)
    // ─────────────────────────────────────────────────────────────────────────

    private static string GenerateCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string GenerateState()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
               .Replace('+', '-')
               .Replace('/', '_')
               .TrimEnd('=');

    // ─────────────────────────────────────────────────────────────────────────

    private string BuildAuthorizationUrl(string challenge, string state, string redirectUri)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["response_type"]          = "code";
        qs["client_id"]              = _clientId;
        qs["scope"]                  = _scopes;
        qs["redirect_uri"]           = redirectUri;
        qs["code_challenge"]         = challenge;
        qs["code_challenge_method"]  = "S256";
        qs["state"]                  = state;

        return $"{_authority}/protocol/openid-connect/auth?{qs}";
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort — the URL is already printed to stderr.
        }
    }
}
