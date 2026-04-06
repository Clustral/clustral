using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral kube login &lt;cluster-id&gt;</c>:
/// <list type="number">
///   <item>Reads the stored Keycloak JWT.</item>
///   <item>POSTs to the ControlPlane <c>/api/v1/credentials/kubeconfig</c>
///     to obtain a short-lived kubeconfig credential.</item>
///   <item>Writes (or updates) the cluster/user/context entry in
///     <c>~/.kube/config</c> via <see cref="KubeconfigWriter"/>.</item>
/// </list>
/// </summary>
internal static class KubeLoginCommand
{
    private static readonly Argument<string> ClusterIdArg = new(
        "cluster-id",
        "ID of the cluster to obtain credentials for.");

    private static readonly Option<string?> ContextNameOption = new(
        "--context-name",
        "Name for the kubeconfig context (default: clustral-<cluster-id>).");

    private static readonly Option<string?> TtlOption = new(
        "--ttl",
        "Requested credential lifetime as an ISO-8601 duration (e.g. PT8H). " +
        "The ControlPlane may cap this to its configured maximum.");

    private static readonly Option<bool> NoSetContextOption = new(
        "--no-set-context",
        "Do not update current-context in kubeconfig after writing the entry.");

    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification for ControlPlane calls (local dev only).");

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <c>kube</c> parent command with <c>login</c> attached.
    /// </summary>
    public static Command BuildKubeCommand()
    {
        var kube = new Command("kube", "Manage kubeconfig credentials for Clustral clusters.");
        kube.AddCommand(BuildLoginSubcommand());
        kube.AddCommand(KubeLogoutCommand.Build());
        kube.AddCommand(KubeLsCommand.Build());
        return kube;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static Command BuildLoginSubcommand()
    {
        var cmd = new Command(
            "login",
            "Issue a short-lived kubeconfig credential and write it to ~/.kube/config.");

        cmd.AddArgument(ClusterIdArg);
        cmd.AddOption(ContextNameOption);
        cmd.AddOption(TtlOption);
        cmd.AddOption(NoSetContextOption);
        cmd.AddOption(InsecureOption);

        cmd.SetHandler(HandleAsync);

        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();

        var config      = CliConfig.Load();
        var clusterId   = ctx.ParseResult.GetValueForArgument(ClusterIdArg);
        var contextName = ctx.ParseResult.GetValueForOption(ContextNameOption)
                          ?? $"clustral-{clusterId}";
        var ttl             = ctx.ParseResult.GetValueForOption(TtlOption);
        var noSetContext    = ctx.ParseResult.GetValueForOption(NoSetContextOption);
        var insecure        = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;
        var controlPlaneUrl = config.ControlPlaneUrl;

        if (string.IsNullOrWhiteSpace(controlPlaneUrl))
        {
            CliErrors.WriteNotConfigured("ControlPlane URL not configured", "clustral login <url>");
            ctx.ExitCode = 1;
            return;
        }

        // ── Read stored JWT ───────────────────────────────────────────────────
        var cache = new TokenCache();
        var token = await cache.ReadAsync(ct);

        if (token is null)
        {
            CliErrors.WriteNotConfigured("Not logged in", "clustral login");
            ctx.ExitCode = 1;
            return;
        }

        // ── Call ControlPlane REST API ────────────────────────────────────────
        IssueCredentialResponse credential;
        try
        {
            credential = await IssueCredentialAsync(
                controlPlaneUrl, token, clusterId, ttl, insecure, ct);
        }
        catch (OperationCanceledException)
        {
            CliErrors.WriteError("Operation cancelled.");
            ctx.ExitCode = 130;
            return;
        }
        catch (Exception ex)
        {
            CliErrors.WriteConnectionError(ex);
            ctx.ExitCode = 1;
            return;
        }

        if (string.IsNullOrEmpty(credential.Token))
        {
            CliErrors.WriteError("ControlPlane returned an empty token. Your session may have expired — run 'clustral login' first.");
            ctx.ExitCode = 1;
            return;
        }

        // ── Write kubeconfig entry ────────────────────────────────────────────
        var serverUrl = $"{controlPlaneUrl.TrimEnd('/')}/api/proxy/{clusterId}";

        var entry = new ClustralKubeconfigEntry(
            ContextName:           contextName,
            ServerUrl:             serverUrl,
            Token:                 credential.Token,
            ExpiresAt:             credential.ExpiresAt,
            InsecureSkipTlsVerify: insecure || serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        try
        {
            var writer = new KubeconfigWriter();
            writer.WriteClusterEntry(entry, setCurrentContext: !noSetContext);
        }
        catch (Exception ex)
        {
            CliErrors.WriteError($"Writing kubeconfig failed: {ex.Message}");
            ctx.ExitCode = 1;
            return;
        }

        AnsiConsole.MarkupLine("\n[green]✓[/] [bold]Kubeconfig updated[/]");
        AnsiConsole.MarkupLine($"  [grey]Context[/]   [cyan]{contextName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Server[/]    {serverUrl.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  [grey]Expires[/]   {credential.ExpiresAt.ToLocalTime():yyyy-MM-dd HH:mm:ss K}");

        if (!noSetContext)
            AnsiConsole.MarkupLine("  [grey]Active[/]    [green]current-context set[/]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REST call
    // ─────────────────────────────────────────────────────────────────────────

    internal static async Task<IssueCredentialResponse> IssueCredentialAsync(
        string            controlPlaneUrl,
        string            bearerToken,
        string            clusterId,
        string?           ttl,
        bool              skipTls,
        CancellationToken ct)
    {
        var handler = skipTls
            ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            : new HttpClientHandler();

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(controlPlaneUrl.TrimEnd('/') + "/"),
        };
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);

        var requestBody = new IssueCredentialRequest
        {
            ClusterId    = clusterId,
            RequestedTtl = ttl,
        };

        var json    = JsonSerializer.Serialize(requestBody, CliJsonContext.Default.IssueCredentialRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await http.PostAsync("api/v1/auth/kubeconfig-credential", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"ControlPlane returned {(int)response.StatusCode}: {detail}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(responseJson, CliJsonContext.Default.IssueCredentialResponse)
               ?? throw new InvalidOperationException("Empty response from ControlPlane.");
    }
}
