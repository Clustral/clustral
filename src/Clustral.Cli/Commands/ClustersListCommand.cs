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
                Console.WriteLine($"  {Ui.Ansi.Dim("No clusters found.")}");
                return;
            }

            Console.WriteLine(
                $"  {Ui.Ansi.Pad(Ui.Ansi.Bold("ID"), 38)}" +
                $"{Ui.Ansi.Pad(Ui.Ansi.Bold("NAME"), 22)}" +
                $"{Ui.Ansi.Pad(Ui.Ansi.Bold("STATUS"), 16)}" +
                $"{Ui.Ansi.Pad(Ui.Ansi.Bold("K8S"), 12)}" +
                $"{Ui.Ansi.Bold("LAST SEEN")}");

            foreach (var c in result.Clusters)
            {
                var lastSeen = c.LastSeenAt.HasValue
                    ? TimeAgo(c.LastSeenAt.Value)
                    : Ui.Ansi.Dim("-");

                var statusText = c.Status switch
                {
                    "Connected" => Ui.Ansi.Green(Ui.Ansi.Dot + " " + c.Status),
                    "Pending" => Ui.Ansi.Yellow(Ui.Ansi.Dot + " " + c.Status),
                    _ => Ui.Ansi.Red(Ui.Ansi.Dot + " " + c.Status),
                };

                Console.WriteLine(
                    $"  {Ui.Ansi.Pad(Ui.Ansi.Dim(c.Id), 38)}" +
                    $"{Ui.Ansi.Pad(Truncate(c.Name, 20), 22)}" +
                    $"{Ui.Ansi.Pad(statusText, 16)}" +
                    $"{Ui.Ansi.Pad(c.KubernetesVersion ?? Ui.Ansi.Dim("-"), 12)}" +
                    $"{lastSeen}");
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
