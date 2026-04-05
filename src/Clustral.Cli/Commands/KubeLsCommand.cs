using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral kube ls</c>: lists available clusters with their
/// connection status, similar to Teleport's <c>tsh kube ls</c>.
/// Shows which cluster the current kubeconfig context points to.
/// </summary>
internal static class KubeLsCommand
{
    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification.");

    public static Command Build()
    {
        var cmd = new Command("ls", "List available Kubernetes clusters.");
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;

        var controlPlaneUrl = config.ControlPlaneUrl;
        if (string.IsNullOrWhiteSpace(controlPlaneUrl))
        {
            await Console.Error.WriteLineAsync(
                "error: ControlPlaneUrl not set. Run 'clustral login <url>' first.");
            ctx.ExitCode = 1;
            return;
        }

        var cache = new TokenCache();
        var token = await cache.ReadAsync(ct);
        if (token is null)
        {
            await Console.Error.WriteLineAsync(
                "error: No token found. Run 'clustral login' first.");
            ctx.ExitCode = 1;
            return;
        }

        var handler = insecure
            ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            : new HttpClientHandler();

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(controlPlaneUrl.TrimEnd('/') + "/"),
        };
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await http.GetAsync("api/v1/clusters", ct);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                await Console.Error.WriteLineAsync($"error: {(int)response.StatusCode} {detail}");
                ctx.ExitCode = 1;
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.ClusterListResponse);

            if (result is null || result.Clusters.Count == 0)
            {
                Console.WriteLine("No Kubernetes clusters found.");
                return;
            }

            // Detect which cluster the current kubeconfig context points to.
            var currentContext = GetCurrentClustralContext();

            Console.WriteLine($"  {"CLUSTER",-24} {"STATUS",-14} {"K8S VERSION",-14} {"LABELS"}");

            foreach (var c in result.Clusters)
            {
                var contextName = $"clustral-{c.Id}";
                var selected = contextName == currentContext ? "> " : "  ";
                var labels = c.Labels.Count > 0
                    ? string.Join(", ", c.Labels.Select(kv => $"{kv.Key}={kv.Value}"))
                    : "";

                Console.WriteLine(
                    $"{selected}{Truncate(c.Name, 24),-24} {c.Status,-14} {c.KubernetesVersion ?? "",-14} {labels}");
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            ctx.ExitCode = 1;
        }
    }

    private static string? GetCurrentClustralContext()
    {
        try
        {
            var kubeconfigPath = KubeconfigWriter.DefaultKubeconfigPath();
            if (!File.Exists(kubeconfigPath)) return null;

            var yaml = File.ReadAllText(kubeconfigPath);
            // Simple extraction — find current-context line.
            foreach (var line in yaml.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("current-context:"))
                {
                    var value = trimmed["current-context:".Length..].Trim().Trim('"', '\'');
                    return value.StartsWith("clustral-") ? value : null;
                }
            }
        }
        catch { }
        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
