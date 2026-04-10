using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Spectre.Console;

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
            CliErrors.WriteNotConfigured(Messages.Errors.ControlPlaneNotConfigured, Messages.Hints.RunLoginWithUrl);
            ctx.ExitCode = 1;
            return;
        }

        var cache = new TokenCache();
        var token = await cache.ReadAsync(ct);
        if (token is null)
        {
            CliErrors.WriteNotConfigured(Messages.Errors.NotLoggedIn, Messages.Hints.RunLogin);
            ctx.ExitCode = 1;
            return;
        }

        var qs = string.IsNullOrEmpty(status) ? "" : $"?statusFilter={status}";

        try
        {
            var result = await CliHttp.RunWithSpinnerAsync(
                Messages.Spinners.LoadingClusters,
                async innerCt =>
                {
                    using var http = CliHttp.CreateClient(controlPlaneUrl, insecure);
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);

                    var response = await http.GetAsync($"api/v1/clusters{qs}", innerCt);
                    if (!response.IsSuccessStatusCode)
                    {
                        var detail = await response.Content.ReadAsStringAsync(innerCt);
                        return ((int?)response.StatusCode, detail, (ClusterListResponse?)null);
                    }

                    var json = await response.Content.ReadAsStringAsync(innerCt);
                    var parsed = JsonSerializer.Deserialize(json, CliJsonContext.Default.ClusterListResponse);
                    return ((int?)null, (string?)null, parsed);
                });

            if (result.Item1 is int code)
            {
                CliErrors.WriteHttpError(code, result.Item2 ?? "");
                ctx.ExitCode = 1;
                return;
            }

            if (result.Item3 is null || result.Item3.Clusters.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No clusters found.[/]");
                return;
            }

            RenderClusterTable(AnsiConsole.Console, result.Item3.Clusters);
        }
        catch (CliHttpTimeoutException)
        {
            CliErrors.WriteError(Messages.Errors.Timeout);
            ctx.ExitCode = 1;
        }
        catch (Exception ex)
        {
            CliErrors.WriteConnectionError(ex);
            ctx.ExitCode = 1;
        }
    }

    internal static void RenderClusterTable(IAnsiConsole console, List<ClusterResponse> clusters)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn("Cluster")
            .AddColumn("ID")
            .AddColumn("Status")
            .AddColumn("Agent Version")
            .AddColumn("K8s Version")
            .AddColumn("Last Seen")
            .AddColumn("Labels");

        foreach (var c in clusters)
        {
            var lastSeen = c.LastSeenAt.HasValue ? TimeAgo(c.LastSeenAt.Value) : "[dim]—[/]";

            var statusMarkup = c.Status switch
            {
                "Connected" => "[green]● Connected[/]",
                "Pending" => "[yellow]● Pending[/]",
                _ => "[red]● Disconnected[/]",
            };

            var agentVersion = c.AgentVersion is not null
                ? $"[cyan]{c.AgentVersion.EscapeMarkup()}[/]"
                : "[dim]—[/]";

            var labels = c.Labels.Count > 0
                ? $"[dim]{string.Join(", ", c.Labels.Select(kv => $"{kv.Key}={kv.Value}")).EscapeMarkup()}[/]"
                : "";

            table.AddRow(
                Truncate(c.Name, 24).EscapeMarkup(),
                $"[dim]{c.Id}[/]",
                statusMarkup,
                agentVersion,
                c.KubernetesVersion ?? "[dim]—[/]",
                lastSeen,
                labels);
        }

        console.Write(table);
    }

    internal static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    internal static string TimeAgo(DateTimeOffset dt)
    {
        var ago = DateTimeOffset.UtcNow - dt;
        if (ago.TotalSeconds < 60) return $"{(int)ago.TotalSeconds}s ago";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24)   return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }
}
