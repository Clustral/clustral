using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;
using Spectre.Console;

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
        var cmd = new Command("list", "List available Kubernetes clusters.");
        cmd.AddAlias("ls");
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

        var result = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.LoadingClusters,
            async innerCt =>
            {
                using var http = CliHttp.CreateClient(controlPlaneUrl, insecure);
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var response = await http.GetAsync("api/v1/clusters", innerCt);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(innerCt);
                    return ((int?)response.StatusCode, detail, (ClusterListResponse?)null);
                }

                var json = await response.Content.ReadAsStringAsync(innerCt);
                var parsed = JsonSerializer.Deserialize(json, CliJsonContext.Default.ClusterListResponse);
                return ((int?)null, (string?)null, parsed);
            });

        if (result.Item1 is int status)
            throw new CliHttpErrorException(status, result.Item2 ?? "");

        if (result.Item3 is null || result.Item3.Clusters.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No Kubernetes clusters found.[/]");
            return;
        }

        var currentContext = GetCurrentClustralContext();
        RenderKubeLsTable(AnsiConsole.Console, result.Item3.Clusters, currentContext);
    }

    internal static void RenderKubeLsTable(
        IAnsiConsole console,
        List<ClusterResponse> clusters,
        string? currentContext)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn("")       // pointer
            .AddColumn("Cluster")
            .AddColumn("ID")
            .AddColumn("Status")
            .AddColumn("Agent Version")
            .AddColumn("K8s Version")
            .AddColumn("Labels");

        foreach (var c in clusters)
        {
            var contextName = $"clustral-{c.Id}";
            var isSelected = contextName == currentContext;
            var pointer = isSelected ? "[green]▸[/]" : " ";

            var statusMarkup = c.Status switch
            {
                "Connected" => "[green]● Connected[/]",
                "Pending" => "[yellow]● Pending[/]",
                _ => "[red]● Disconnected[/]",
            };

            var clusterName = isSelected
                ? $"[bold]{Truncate(c.Name, 24).EscapeMarkup()}[/]"
                : Truncate(c.Name, 24).EscapeMarkup();

            var agentVersion = c.AgentVersion is not null
                ? $"[cyan]{c.AgentVersion.EscapeMarkup()}[/]"
                : "[dim]—[/]";

            var labels = c.Labels.Count > 0
                ? $"[dim]{string.Join(", ", c.Labels.Select(kv => $"{kv.Key}={kv.Value}")).EscapeMarkup()}[/]"
                : "";

            table.AddRow(
                pointer,
                clusterName,
                $"[dim]{c.Id}[/]",
                statusMarkup,
                agentVersion,
                c.KubernetesVersion ?? "[dim]—[/]",
                labels);
        }

        console.Write(table);
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
