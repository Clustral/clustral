using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Clustral.Cli.Auth;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;

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

    // ─────────────────────────────────────────────────────────────────────────

    public static Command Build()
    {
        var cmd = new Command("login",
            "Authenticate via the ControlPlane. " +
            "Discovers Keycloak automatically and opens the browser for SSO login.");

        cmd.AddArgument(ControlPlaneArg);
        cmd.AddOption(PortOption);
        cmd.AddOption(InsecureOption);

        cmd.SetHandler(HandleAsync);

        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();

        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;
        var port     = ctx.ParseResult.GetValueForOption(PortOption)     ?? config.CallbackPort;

        // ── Resolve ControlPlane URL ──────────────────────────────────────
        var cpUrl = ctx.ParseResult.GetValueForArgument(ControlPlaneArg);
        if (string.IsNullOrWhiteSpace(cpUrl))
            cpUrl = config.ControlPlaneUrl;

        if (string.IsNullOrWhiteSpace(cpUrl))
        {
            await Console.Error.WriteLineAsync(
                "error: Provide a ControlPlane URL, e.g.:\n" +
                "  clustral login controlplane.clustral.example\n" +
                "  clustral login http://localhost:5100\n\n" +
                "Or set ControlPlaneUrl in ~/.clustral/config.json.");
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

        var httpHandler = insecure
            ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            : new HttpClientHandler();
        using var http = new HttpClient(httpHandler);

        // ── Discover OIDC configuration from ControlPlane ─────────────────
        Console.Error.WriteLine($"Connecting to {cpUrl}...");

        var discovered = await DiscoverConfigAsync(http, cpUrl, ct);
        if (discovered is null)
        {
            await Console.Error.WriteLineAsync("error: Could not reach the ControlPlane.");
            ctx.ExitCode = 1;
            return;
        }

        // ── Save ControlPlane URL for future commands ─────────────────────
        if (config.ControlPlaneUrl != cpUrl)
        {
            config.ControlPlaneUrl = cpUrl;
            config.Save();
        }

        // ── Run OIDC PKCE flow ────────────────────────────────────────────
        var flow = new OidcFlowHandler(
            discovered.OidcAuthority,
            discovered.OidcClientId,
            discovered.OidcScopes,
            port,
            http);

        try
        {
            var token = await flow.LoginAsync(ct);

            var cache = new TokenCache();
            await cache.StoreAsync(token, ct);

            Console.WriteLine($"Logged in to {cpUrl}");
            Console.WriteLine("Token stored in ~/.clustral/token.");
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Login cancelled.");
            ctx.ExitCode = 130;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            ctx.ExitCode = 1;
        }
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
}
