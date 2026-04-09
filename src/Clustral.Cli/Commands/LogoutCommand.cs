using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral logout</c>:
/// <list type="number">
///   <item>Revokes all Clustral kubeconfig credentials on the ControlPlane.</item>
///   <item>Removes all <c>clustral-*</c> contexts from <c>~/.kube/config</c>.</item>
///   <item>Clears the stored JWT from <c>~/.clustral/token</c>.</item>
/// </list>
/// </summary>
internal static class LogoutCommand
{
    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification.");

    public static Command Build()
    {
        var cmd = new Command("logout", "Sign out — revoke credentials and remove kubeconfig contexts.");
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;

        // ── 1. Read kubeconfig + collect clustral contexts (local) ────────
        var kubeconfigPath   = KubeconfigWriter.DefaultKubeconfigPath();
        var writer           = new KubeconfigWriter(kubeconfigPath);
        var clustralContexts = FindClustralContexts(kubeconfigPath);
        var cache            = new TokenCache();
        var jwt              = await cache.ReadAsync(ct);

        // ── 2. Local cleanup FIRST — instant, no network ──────────────────
        foreach (var (contextName, _) in clustralContexts)
        {
            writer.RemoveClusterEntry(contextName);
            AnsiConsole.MarkupLine($"  [red]✗[/] Removed kubeconfig context: [cyan]{contextName.EscapeMarkup()}[/]");
        }

        await cache.ClearAsync(ct);
        AnsiConsole.MarkupLine("\n[green]✓[/] [bold]Logged out locally.[/]");

        // ── 3. Best-effort remote revocation with spinner + 5s timeout ────
        var tokensToRevoke = clustralContexts
            .Where(c => !string.IsNullOrEmpty(c.Token))
            .ToList();

        if (jwt is null || string.IsNullOrWhiteSpace(config.ControlPlaneUrl) || tokensToRevoke.Count == 0)
        {
            return;
        }

        var revoked = 0;
        try
        {
            await CliHttp.RunWithSpinnerAsync(
                $"Revoking {tokensToRevoke.Count} credential(s) on ControlPlane...",
                async innerCt =>
                {
                    using var http = CliHttp.CreateClient(config.ControlPlaneUrl, insecure);
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", jwt);

                    foreach (var (contextName, token) in tokensToRevoke)
                    {
                        try
                        {
                            var body = JsonSerializer.Serialize(
                                new RevokeByTokenRequest { Token = token! },
                                CliJsonContext.Default.RevokeByTokenRequest);
                            using var content = new StringContent(body, Encoding.UTF8, "application/json");
                            var response = await http.PostAsync("api/v1/auth/revoke-by-token", content, innerCt);
                            if (response.IsSuccessStatusCode)
                                revoked++;
                        }
                        catch (Exception ex) when (!CliHttp.IsTimeout(ex))
                        {
                            // Per-token error — continue with the next token.
                        }
                    }
                },
                ct);

            if (revoked > 0)
                AnsiConsole.MarkupLine($"[green]✓[/] Revoked {revoked} credential(s) on ControlPlane.");
            else
                AnsiConsole.MarkupLine("[yellow]![/] No credentials revoked on ControlPlane.");
        }
        catch (CliHttpTimeoutException)
        {
            AnsiConsole.MarkupLine(
                "[yellow]![/] Could not reach ControlPlane — local logout complete. " +
                "[dim]Server-side credentials will expire on their own.[/]");
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine(
                "[yellow]![/] ControlPlane revocation failed — local logout complete.");
        }
    }

    /// <summary>
    /// Finds all <c>clustral-*</c> contexts and their tokens from the kubeconfig.
    /// </summary>
    internal static List<(string ContextName, string? Token)> FindClustralContexts(string kubeconfigPath)
    {
        var results = new List<(string, string?)>();

        if (!File.Exists(kubeconfigPath))
            return results;

        try
        {
            var yaml = File.ReadAllText(kubeconfigPath);
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            var doc = deserializer.Deserialize<Dictionary<object, object>>(yaml);
            if (doc is null) return results;

            // Find all clustral-* context names.
            if (doc.TryGetValue("contexts", out var ctxRaw) && ctxRaw is List<object> contexts)
            {
                foreach (var item in contexts.OfType<Dictionary<object, object>>())
                {
                    if (item.TryGetValue("name", out var n) && n is string name &&
                        name.StartsWith("clustral-", StringComparison.Ordinal))
                    {
                        // Find the matching user token.
                        string? token = null;
                        if (doc.TryGetValue("users", out var usersRaw) && usersRaw is List<object> users)
                        {
                            var user = users.OfType<Dictionary<object, object>>()
                                .FirstOrDefault(u => u.TryGetValue("name", out var un) && un is string us && us == name);
                            if (user?.TryGetValue("user", out var userData) == true &&
                                userData is Dictionary<object, object> ud &&
                                ud.TryGetValue("token", out var t) && t is string tokenStr)
                            {
                                token = tokenStr;
                            }
                        }
                        results.Add((name, token));
                    }
                }
            }
        }
        catch
        {
            // Best effort.
        }

        return results;
    }
}
