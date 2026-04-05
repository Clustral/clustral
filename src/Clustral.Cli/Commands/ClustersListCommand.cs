using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral clusters list</c>: lists registered clusters
/// from the ControlPlane REST API.
/// </summary>
internal static class ClustersListCommand
{
    private static readonly Option<string?> StatusOption = new(
        "--status",
        "Filter by status (Pending, Connected, Disconnected).");

    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification.");

    public static Command BuildClustersCommand()
    {
        var clusters = new Command("clusters", "Manage Clustral clusters.");
        clusters.AddCommand(BuildListSubcommand());
        return clusters;
    }

    private static Command BuildListSubcommand()
    {
        var cmd = new Command("list", "List registered clusters.");
        cmd.AddAlias("ls");
        cmd.AddOption(StatusOption);
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var config   = CliConfig.Load();
        var status   = ctx.ParseResult.GetValueForOption(StatusOption);
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

        var qs = string.IsNullOrEmpty(status) ? "" : $"?statusFilter={status}";

        try
        {
            var response = await http.GetAsync($"api/v1/clusters{qs}", ct);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                await Console.Error.WriteLineAsync(
                    $"error: {(int)response.StatusCode} {detail}");
                ctx.ExitCode = 1;
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.ClusterListResponse);

            if (result is null || result.Clusters.Count == 0)
            {
                Console.WriteLine("No clusters found.");
                return;
            }

            // Header
            Console.WriteLine($"{"ID",-38} {"NAME",-20} {"STATUS",-14} {"K8S",-10} {"LAST SEEN"}");

            foreach (var c in result.Clusters)
            {
                var lastSeen = c.LastSeenAt.HasValue
                    ? TimeAgo(c.LastSeenAt.Value)
                    : "-";

                Console.WriteLine(
                    $"{c.Id,-38} {Truncate(c.Name, 20),-20} {c.Status,-14} {c.KubernetesVersion ?? "-",-10} {lastSeen}");
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            ctx.ExitCode = 1;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string TimeAgo(DateTimeOffset dt)
    {
        var ago = DateTimeOffset.UtcNow - dt;
        if (ago.TotalSeconds < 60) return $"{(int)ago.TotalSeconds}s ago";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24)   return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }
}
