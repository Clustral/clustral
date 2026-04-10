using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Auth;

/// <summary>
/// Ensures the CLI has a valid JWT session before making authenticated calls.
/// If the token is missing or expired, prompts the user to re-login inline
/// (when running interactively) instead of just showing an error.
/// </summary>
internal static class SessionHelper
{
    /// <summary>
    /// Returns a valid JWT, or <c>null</c> if the user is not logged in and
    /// declined (or couldn't be prompted for) re-login.
    ///
    /// Flow:
    /// <list type="number">
    ///   <item>Read token from <c>~/.clustral/token</c>.</item>
    ///   <item>If valid (not expired), return it immediately.</item>
    ///   <item>If expired/missing AND interactive terminal AND OIDC config
    ///     available: prompt "Session expired. Login again? [Y/n]".</item>
    ///   <item>If yes: run OIDC PKCE flow inline, store new token, return it.</item>
    ///   <item>If no / non-interactive / no OIDC config: return <c>null</c>.</item>
    /// </list>
    /// </summary>
    public static async Task<string?> EnsureValidTokenAsync(
        CliConfig config, bool insecure, CancellationToken ct)
    {
        var cache = new TokenCache(CliConfig.DefaultTokenPath);
        var token = await cache.ReadAsync(ct);

        // ── Check if current token is still valid ────────────────────────
        if (token is not null)
        {
            var expiry = Commands.LoginCommand.DecodeJwtExpiry(token);
            if (expiry.HasValue && expiry.Value > DateTimeOffset.UtcNow)
            {
                CliDebug.Log($"JWT valid until {expiry.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                return token;
            }
            CliDebug.Log("JWT expired or expiry unreadable");
        }
        else
        {
            CliDebug.Log("No JWT found in ~/.clustral/token");
        }

        // ── Can we prompt for re-login? ──────────────────────────────────
        if (Console.IsOutputRedirected || Console.IsInputRedirected)
        {
            CliDebug.Log("Non-interactive session — cannot prompt for re-login");
            return null;
        }

        if (string.IsNullOrWhiteSpace(config.OidcAuthority) ||
            string.IsNullOrWhiteSpace(config.ControlPlaneUrl))
        {
            CliDebug.Log("OIDC settings not configured — cannot auto-login");
            return null;
        }

        // ── Prompt ───────────────────────────────────────────────────────
        var message = token is not null
            ? "Session expired. Login again?"
            : "Not logged in. Login now?";

        var confirm = AnsiConsole.Prompt(
            new TextPrompt<bool>(message)
                .AddChoice(true).AddChoice(false)
                .DefaultValue(true)
                .WithConverter(v => v ? "y" : "n")
                .PromptStyle(Style.Plain));
        if (!confirm)
            return null;

        // ── Run OIDC PKCE flow inline ────────────────────────────────────
        CliDebug.Log($"Starting inline re-login via {config.OidcAuthority}");

        var httpHandler = insecure
            ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            : new HttpClientHandler();
        HttpMessageHandler pipeline = CliDebug.Enabled
            ? new Http.DebugLoggingHandler(httpHandler)
            : httpHandler;
        using var http = new HttpClient(pipeline) { Timeout = TimeSpan.FromSeconds(10) };

        var flow = new OidcFlowHandler(
            config.OidcAuthority,
            config.OidcClientId,
            config.OidcScopes,
            config.CallbackPort,
            http);

        var newToken = await flow.LoginAsync(ct);
        await cache.StoreAsync(newToken, ct);

        AnsiConsole.MarkupLine($"\n[green]✓[/] [bold]{Messages.Success.LoggedIn}[/]\n");

        return newToken;
    }
}
