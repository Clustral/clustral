using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;

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

        // ── 1. Read kubeconfig and find all clustral contexts ─────────────
        var kubeconfigPath = KubeconfigWriter.DefaultKubeconfigPath();
        var writer = new KubeconfigWriter(kubeconfigPath);
        var clustralContexts = FindClustralContexts(kubeconfigPath);

        // ── 2. Revoke tokens on ControlPlane ──────────────────────────────
        var cache = new TokenCache();
        var jwt = await cache.ReadAsync(ct);

        if (jwt is not null && !string.IsNullOrWhiteSpace(config.ControlPlaneUrl))
        {
            var handler = insecure
                ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
                : new HttpClientHandler();

            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(config.ControlPlaneUrl.TrimEnd('/') + "/"),
            };
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", jwt);

            // Revoke each kubeconfig token.
            foreach (var (contextName, token) in clustralContexts)
            {
                if (string.IsNullOrEmpty(token)) continue;

                try
                {
                    var body = JsonSerializer.Serialize(
                        new { token },
                        CliJsonContext.Default.Options);
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await http.PostAsync("api/v1/auth/revoke-by-token", content, ct);

                    if (response.IsSuccessStatusCode)
                        Console.WriteLine($"  {Ui.Ansi.Red(Ui.Ansi.Cross)} Revoked credential for {Ui.Ansi.Cyan(contextName)}");
                }
                catch
                {
                    // Best effort — continue even if revocation fails.
                }
            }
        }

        // ── 3. Remove clustral contexts from kubeconfig ───────────────────
        foreach (var (contextName, _) in clustralContexts)
        {
            writer.RemoveClusterEntry(contextName);
            Console.WriteLine($"  {Ui.Ansi.Red(Ui.Ansi.Cross)} Removed kubeconfig context: {Ui.Ansi.Cyan(contextName)}");
        }

        // ── 4. Clear the JWT ──────────────────────────────────────────────
        await cache.ClearAsync(ct);

        Console.WriteLine($"\n  {Ui.Ansi.Green(Ui.Ansi.Check)} {Ui.Ansi.Bold("Logged out.")}");

    }

    /// <summary>
    /// Finds all <c>clustral-*</c> contexts and their tokens from the kubeconfig.
    /// </summary>
    private static List<(string ContextName, string? Token)> FindClustralContexts(string kubeconfigPath)
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
