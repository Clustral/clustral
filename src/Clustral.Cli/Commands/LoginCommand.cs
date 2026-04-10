using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Clustral.Cli.Auth;
using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral login &lt;controlplane-url&gt;</c>.
/// Discovers OIDC settings from the ControlPlane, then opens the browser
/// for Keycloak PKCE authentication and stores the JWT.
/// </summary>
internal static class LoginCommand
{
    private static readonly Argument<string> ControlPlaneArg = new(
        "controlplane-url",
        "ControlPlane URL (e.g. controlplane.clustral.example). " +
        "Defaults to ControlPlaneUrl in ~/.clustral/config.json.")
    {
        Arity = ArgumentArity.ZeroOrOne,
    };

    private static readonly Option<int?> PortOption = new(
        "--port",
        "Local TCP port for the OIDC callback listener. Defaults to 7777.");

    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification (local dev only).");

    private static readonly Option<bool> ForceOption = new(
        "--force",
        "Force re-authentication even if already logged in.");

    // ─────────────────────────────────────────────────────────────────────────

    public static Command Build()
    {
        var cmd = new Command("login",
            "Authenticate via the ControlPlane. " +
            "Shows current session if already logged in. Use --force to re-authenticate.");

        cmd.AddArgument(ControlPlaneArg);
        cmd.AddOption(PortOption);
        cmd.AddOption(InsecureOption);
        cmd.AddOption(ForceOption);

        cmd.SetHandler(HandleAsync);

        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();

        var config   = CliConfig.Load();
        CliDebug.Log("Loaded config from ~/.clustral/config.json");
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;
        var port     = ctx.ParseResult.GetValueForOption(PortOption)     ?? config.CallbackPort;
        var force    = ctx.ParseResult.GetValueForOption(ForceOption);

        // ── Resolve ControlPlane URL ──────────────────────────────────────
        var cpUrl = ctx.ParseResult.GetValueForArgument(ControlPlaneArg);
        if (string.IsNullOrWhiteSpace(cpUrl))
            cpUrl = config.ControlPlaneUrl;

        if (string.IsNullOrWhiteSpace(cpUrl))
        {
            CliErrors.WriteNotConfigured(Messages.Errors.ControlPlaneNotConfigured,
                Messages.Hints.RunLoginWithUrl);
            ctx.ExitCode = 1;
            return;
        }

        // Normalize: add scheme if not provided.
        // Default to http:// for localhost/127.0.0.1, https:// for everything else.
        if (!cpUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !cpUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var isLocal = cpUrl.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                          cpUrl.StartsWith("127.0.0.1", StringComparison.Ordinal);
            cpUrl = isLocal ? $"http://{cpUrl}" : $"https://{cpUrl}";
        }

        CliDebug.Log($"ControlPlane URL: {cpUrl}");

        var httpHandler = insecure
            ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            : new HttpClientHandler();
        HttpMessageHandler pipeline = CliDebug.Enabled
            ? new Http.DebugLoggingHandler(httpHandler)
            : httpHandler;
        using var http = new HttpClient(pipeline)
        {
            // Bound every HTTP call so an unreachable ControlPlane fails fast.
            Timeout = TimeSpan.FromSeconds(10),
        };

        // ── Check for existing valid session ──────────────────────────────
        CliDebug.Log("Checking for existing valid session...");
        if (!force)
        {
            var cache = new TokenCache(CliConfig.DefaultTokenPath);
            var existingToken = await cache.ReadAsync(ct);

            if (existingToken is not null)
            {
                var expiry = DecodeJwtExpiry(existingToken);
                if (expiry.HasValue && expiry.Value > DateTimeOffset.UtcNow)
                {
                    CliDebug.Log($"Existing session valid until {expiry.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                    // Session still valid — show profile and exit.
                    await DisplayProfileAsync(http, cpUrl, existingToken, ct);
                    return;
                }
            }
        }

        // ── Discover OIDC configuration from ControlPlane ─────────────────
        var discovered = await Http.CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.DiscoveringConfig(cpUrl),
            innerCt => DiscoverConfigAsync(http, cpUrl, innerCt),
            ct);

        if (discovered is null)
        {
            CliErrors.WriteError(Messages.Errors.Timeout);
            ctx.ExitCode = 1;
            return;
        }

        // ── Resolve effective ControlPlane URL ──────────────────────────
        // If the discovery response includes a direct ControlPlane URL,
        // use that so all subsequent CLI calls bypass the Web UI proxy.
        var effectiveCpUrl = !string.IsNullOrWhiteSpace(discovered.ControlPlaneUrl)
            ? discovered.ControlPlaneUrl.TrimEnd('/')
            : cpUrl;

        config.ControlPlaneUrl = effectiveCpUrl;
        if (!string.IsNullOrWhiteSpace(discovered.OidcAuthority))
            config.OidcAuthority = discovered.OidcAuthority;
        if (!string.IsNullOrWhiteSpace(discovered.OidcClientId))
            config.OidcClientId = discovered.OidcClientId;
        if (!string.IsNullOrWhiteSpace(discovered.OidcScopes))
            config.OidcScopes = discovered.OidcScopes;
        config.Save();

        // ── Run OIDC PKCE flow ────────────────────────────────────────────
        var flow = new OidcFlowHandler(
            discovered.OidcAuthority,
            discovered.OidcClientId,
            discovered.OidcScopes,
            port,
            http);

        CliDebug.Log($"Starting OIDC callback listener on 127.0.0.1:{port}");
        var token = await flow.LoginAsync(ct);

        // Store token under accounts/{email}.token for multi-account support.
        var (loginEmail, _) = WhoamiCommand.DecodeEmailAndExpiry(token);
        if (loginEmail is not null)
        {
            CliDebug.Log($"Storing JWT for account {loginEmail}");
            AccountsCommand.StoreAccountToken(loginEmail, token);
        }
        else
        {
            // Fallback: can't decode email → store as legacy single token.
            CliDebug.Log("Storing JWT in legacy token path (no email claim)");
            var tokenCache = new TokenCache(CliConfig.DefaultTokenPath);
            await tokenCache.StoreAsync(token, ct);
        }

        // Fetch and display user profile.
        await DisplayProfileAsync(http, effectiveCpUrl, token, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static readonly string[] DiscoveryPaths =
    [
        "/.well-known/clustral-configuration",   // unified domain (app.clustral.dev)
        "/api/v1/config",                        // direct ControlPlane
    ];

    private static async Task<ControlPlaneConfig?> DiscoverConfigAsync(
        HttpClient http, string baseUrl, CancellationToken ct)
    {
        var root = baseUrl.TrimEnd('/');

        foreach (var path in DiscoveryPaths)
        {
            try
            {
                var response = await http.GetAsync($"{root}{path}", ct);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync(ct);
                var cfg  = JsonSerializer.Deserialize(json, CliJsonContext.Default.ControlPlaneConfig);
                if (cfg is not null && !string.IsNullOrEmpty(cfg.OidcAuthority))
                    return cfg;
            }
            catch
            {
                // Try next path.
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task DisplayProfileAsync(
        HttpClient http, string cpUrl, string token, CancellationToken ct)
    {
        try
        {
            var url = $"{cpUrl.TrimEnd('/')}/api/v1/users/me";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

            var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Fallback: just show basic info.
                AnsiConsole.MarkupLine($"\n[green]✓[/] Logged in to [cyan]{cpUrl.EscapeMarkup()}[/]");
                AnsiConsole.MarkupLine($"  [dim](profile unavailable: {(int)response.StatusCode})[/]");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var profile = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserProfileResponse);

            if (profile is null)
            {
                Console.WriteLine($"\n  Logged in to {cpUrl}");
                return;
            }

            // Decode JWT expiry.
            var expiry = DecodeJwtExpiry(token);

            // Collect unique roles and clusters.
            var roles = profile.Assignments
                .Select(a => a.RoleName)
                .Distinct()
                .OrderBy(r => r)
                .ToList();

            var clusters = profile.Assignments
                .Select(a => a.ClusterName)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // Display profile with Spectre.Console.
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✓[/] [bold]{Messages.Success.LoggedIn}[/]{ProfileCommand.GetProfileBadge()}");
            AnsiConsole.WriteLine();

            RenderProfileTable(AnsiConsole.Console, profile, cpUrl, roles, clusters, expiry);
        }
        catch
        {
            Console.WriteLine($"\n  Logged in to {cpUrl}");
        }
    }

    internal static DateTimeOffset? DecodeJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            // Pad base64.
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
                payload.Replace('-', '+').Replace('_', '/')));
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var exp))
                return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
        }
        catch { }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extracted for testability — accepts IAnsiConsole so tests can capture output.
    // ─────────────────────────────────────────────────────────────────────────

    internal static void RenderProfileTable(
        IAnsiConsole console,
        UserProfileResponse profile,
        string cpUrl,
        List<string> roles,
        List<string> clusters,
        DateTimeOffset? expiry = null)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Key").PadRight(2))
            .AddColumn(new TableColumn("Value"));

        table.AddRow("[grey]Profile URL[/]", $"[cyan]{cpUrl.EscapeMarkup()}[/]");
        table.AddRow("[grey]Logged in as[/]", $"[bold]{(profile.DisplayName ?? profile.Email).EscapeMarkup()}[/]");
        table.AddRow("[grey]Email[/]", profile.Email.EscapeMarkup());
        table.AddRow("[grey]Kubernetes[/]", "[green]enabled[/]");
        table.AddRow("[grey]CLI version[/]", $"v{VersionCommand.GetVersion()}");

        if (expiry.HasValue)
        {
            var remaining = expiry.Value - DateTimeOffset.UtcNow;
            var validFor = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                : $"{(int)remaining.TotalMinutes}m";
            var color = remaining.TotalMinutes < 10 ? "red"
                      : remaining.TotalHours < 1   ? "yellow"
                      : "green";
            table.AddRow("[grey]Valid until[/]",
                $"{expiry.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss K} [[[{color}]valid for {validFor}[/]]]");
        }

        table.AddRow("[grey]Roles[/]", roles.Count > 0
            ? $"[yellow]{string.Join(", ", roles).EscapeMarkup()}[/]"
            : "[dim](none assigned)[/]");

        table.AddRow("[grey]Clusters[/]", clusters.Count > 0
            ? $"[cyan]{string.Join(", ", clusters).EscapeMarkup()}[/]"
            : "[dim](none assigned)[/]");

        var hasAccess = profile.Assignments.Count > 0 || profile.ActiveGrants.Count > 0;
        if (hasAccess)
        {
            var first = true;
            foreach (var a in profile.Assignments)
            {
                var label = first ? "[grey]Access[/]" : "";
                table.AddRow(label, $"[cyan]{a.ClusterName.EscapeMarkup()}[/] [dim]→[/] [yellow]{a.RoleName.EscapeMarkup()}[/]");
                first = false;
            }
            foreach (var g in profile.ActiveGrants)
            {
                var label = first ? "[grey]Access[/]" : "";
                var remaining = g.GrantExpiresAt - DateTimeOffset.UtcNow;
                var validFor = remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                    : $"{(int)remaining.TotalMinutes}m";
                table.AddRow(label,
                    $"[cyan]{g.ClusterName.EscapeMarkup()}[/] [dim]→[/] [yellow]{g.RoleName.EscapeMarkup()}[/] [dim][[JIT {validFor} remaining]][/]");
                first = false;
            }
        }

        console.Write(table);
    }
}
