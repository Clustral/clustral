using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral kube logout &lt;cluster&gt;</c>:
/// <list type="number">
///   <item>Finds the <c>clustral-{cluster}</c> context in <c>~/.kube/config</c>.</item>
///   <item>Revokes the kubeconfig credential token on the ControlPlane.</item>
///   <item>Removes the cluster/user/context entry from <c>~/.kube/config</c>.</item>
/// </list>
/// </summary>
internal static class KubeLogoutCommand
{
    private static readonly Argument<string> ClusterArg = new(
        "cluster",
        "Cluster name or ID to disconnect from (context name: clustral-<cluster>).");

    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification.");

    public static Command Build()
    {
        var cmd = new Command("logout", "Disconnect from a cluster — revoke credential and remove kubeconfig context.");
        cmd.AddArgument(ClusterArg);
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;
        var cluster  = ctx.ParseResult.GetValueForArgument(ClusterArg);

        // Resolve context name: if user passed a raw name, prefix with "clustral-".
        var contextName = cluster.StartsWith("clustral-", StringComparison.Ordinal)
            ? cluster
            : $"clustral-{cluster}";

        // ── 1. Find the context and its token in kubeconfig (local) ──────
        var kubeconfigPath = KubeconfigWriter.DefaultKubeconfigPath();
        var allContexts = LogoutCommand.FindClustralContexts(kubeconfigPath);
        var match = allContexts.FirstOrDefault(c => c.ContextName == contextName);

        if (match.ContextName is null)
        {
            AnsiConsole.MarkupLine($"[yellow]![/] {Messages.Warnings.ContextNotFound(contextName)}");
            return;
        }

        // ── 2. Local cleanup FIRST — instant, no network ─────────────────
        var writer = new KubeconfigWriter(kubeconfigPath);
        writer.RemoveClusterEntry(contextName);
        AnsiConsole.MarkupLine($"  [red]✗[/] Removed kubeconfig context [cyan]{contextName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"\n[green]✓[/] [bold]Disconnected from {contextName.EscapeMarkup()}[/]");

        // ── 3. Best-effort remote revocation with spinner + 5s timeout ───
        if (match.Token is null || string.IsNullOrWhiteSpace(config.ControlPlaneUrl))
            return;

        var cache = new TokenCache();
        var jwt = await cache.ReadAsync(ct);
        if (jwt is null) return;

        try
        {
            await CliHttp.RunWithSpinnerAsync(
                Messages.Spinners.RevokingGrant,
                async innerCt =>
                {
                    using var http = CliHttp.CreateClient(config.ControlPlaneUrl, insecure);
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", jwt);

                    var body = JsonSerializer.Serialize(
                        new RevokeByTokenRequest { Token = match.Token },
                        CliJsonContext.Default.RevokeByTokenRequest);
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = await http.PostAsync("api/v1/auth/revoke-by-token", content, innerCt);

                    if (response.IsSuccessStatusCode)
                        AnsiConsole.MarkupLine($"[green]✓[/] Revoked credential on ControlPlane.");
                    else
                        AnsiConsole.MarkupLine($"[yellow]![/] Credential revocation returned {(int)response.StatusCode} (continuing).");
                },
                ct);
        }
        catch (CliHttpTimeoutException)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]![/] {Messages.Warnings.ControlPlaneUnreachable} " +
                $"[dim]{Messages.Warnings.CredentialsExpireNaturally}[/]");
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]![/] {Messages.Warnings.RevocationFailed}");
        }
    }
}
