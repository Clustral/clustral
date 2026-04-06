using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Cli.Validation;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral access</c> — Just-In-Time access request commands.
/// </summary>
internal static class AccessCommand
{
    // ── Options shared across subcommands ─────────────────────────────────────

    private static readonly Option<bool> InsecureOption = new(
        "--insecure", "Skip TLS verification.");

    // ── access request ───────────────────────────────────────────────────────

    private static readonly Option<string> RoleOption = new(
        "--role", "Role name to request.") { IsRequired = true };

    private static readonly Option<string> ClusterOption = new(
        "--cluster", "Cluster name or ID.") { IsRequired = true };

    private static readonly Option<string?> ReasonOption = new(
        "--reason", "Justification for the access request.");

    private static readonly Option<string?> DurationOption = new(
        "--duration", "Requested access duration as ISO 8601 (e.g., PT8H). Default: PT8H.");

    private static readonly Option<string[]?> ReviewerOption = new(
        "--reviewer", "Reviewer email(s). Repeatable.") { AllowMultipleArgumentsPerToken = true };

    private static readonly Option<bool> WaitOption = new(
        "--wait", "Block until the request is approved or denied.");

    // ── access ls ────────────────────────────────────────────────────────────

    private static readonly Option<string?> StatusFilterOption = new(
        "--status", "Filter by status (Pending, Approved, Denied, Expired, Revoked).");

    private static readonly Option<bool> ActiveOption = new(
        "--active", "Show only active grants (approved, not expired, not revoked).");

    // ── access deny ──────────────────────────────────────────────────────────

    private static readonly Option<string> DenyReasonOption = new(
        "--reason", "Reason for denial.") { IsRequired = true };

    // ─────────────────────────────────────────────────────────────────────────

    public static Command BuildAccessCommand()
    {
        var access = new Command("access", "Manage Just-In-Time access requests.");
        access.AddCommand(BuildRequestSubcommand());
        access.AddCommand(BuildLsSubcommand());
        access.AddCommand(BuildApproveSubcommand());
        access.AddCommand(BuildDenySubcommand());
        access.AddCommand(BuildRevokeSubcommand());
        return access;
    }

    private static Command BuildRequestSubcommand()
    {
        var cmd = new Command("request", "Request temporary access to a cluster.");
        cmd.AddOption(RoleOption);
        cmd.AddOption(ClusterOption);
        cmd.AddOption(ReasonOption);
        cmd.AddOption(DurationOption);
        cmd.AddOption(ReviewerOption);
        cmd.AddOption(WaitOption);
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleRequestAsync);
        return cmd;
    }

    private static Command BuildLsSubcommand()
    {
        var cmd = new Command("list", "List access requests.");
        cmd.AddAlias("ls");
        cmd.AddOption(StatusFilterOption);
        cmd.AddOption(ActiveOption);
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleLsAsync);
        return cmd;
    }

    private static Command BuildApproveSubcommand()
    {
        var cmd = new Command("approve", "Approve a pending access request.");
        cmd.AddArgument(new Argument<string>("request-id", "ID of the access request to approve."));
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleApproveAsync);
        return cmd;
    }

    private static Command BuildDenySubcommand()
    {
        var cmd = new Command("deny", "Deny a pending access request.");
        cmd.AddArgument(new Argument<string>("request-id", "ID of the access request to deny."));
        cmd.AddOption(DenyReasonOption);
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleDenyAsync);
        return cmd;
    }

    private static Command BuildRevokeSubcommand()
    {
        var cmd = new Command("revoke", "Revoke an active access grant.");
        cmd.AddArgument(new Argument<string>("request-id", "ID of the access request/grant to revoke."));
        cmd.AddOption(new Option<string?>("--reason", "Reason for revocation."));
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleRevokeAsync);
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task HandleRequestAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();
        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var roleName    = ctx.ParseResult.GetValueForOption(RoleOption)!;
        var clusterName = ctx.ParseResult.GetValueForOption(ClusterOption)!;
        var reason      = ctx.ParseResult.GetValueForOption(ReasonOption);
        var duration    = ctx.ParseResult.GetValueForOption(DurationOption);
        var reviewers   = ctx.ParseResult.GetValueForOption(ReviewerOption);
        var wait        = ctx.ParseResult.GetValueForOption(WaitOption);

        // Validate input before making HTTP calls.
        var input = new AccessRequestInput(roleName, clusterName, duration);
        if (!ValidationHelper.Validate(AnsiConsole.Console, new AccessRequestValidator(), input, ctx))
            return;

        // Resolve role name → ID.
        var rolesJson = await http.GetStringAsync("api/v1/roles", ct);
        var rolesResp = JsonDocument.Parse(rolesJson).RootElement;
        string? roleId = null;
        if (rolesResp.TryGetProperty("roles", out var rolesArr))
        {
            foreach (var r in rolesArr.EnumerateArray())
            {
                if (r.GetProperty("name").GetString()?.Equals(roleName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    roleId = r.GetProperty("id").GetString();
                    break;
                }
            }
        }
        if (roleId is null)
        {
            CliErrors.WriteError($"Role '{roleName}' not found.");
            ctx.ExitCode = 1;
            return;
        }

        // Resolve cluster name → ID.
        var clustersJson = await http.GetStringAsync("api/v1/clusters", ct);
        var clustersResp = JsonSerializer.Deserialize(clustersJson, CliJsonContext.Default.ClusterListResponse);
        var cluster = clustersResp?.Clusters.FirstOrDefault(c =>
            c.Name.Equals(clusterName, StringComparison.OrdinalIgnoreCase) || c.Id == clusterName);
        if (cluster is null)
        {
            CliErrors.WriteError($"Cluster '{clusterName}' not found.");
            ctx.ExitCode = 1;
            return;
        }

        var body = new AccessRequestCreateRequest
        {
            RoleId = roleId,
            ClusterId = cluster.Id,
            Reason = reason,
            RequestedDuration = duration,
            SuggestedReviewerEmails = reviewers?.ToList(),
        };

        var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestCreateRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync("api/v1/access-requests", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            CliErrors.WriteHttpError((int)response.StatusCode, detail);
            ctx.ExitCode = 1;
            return;
        }

        var respJson = await response.Content.ReadAsStringAsync(ct);
        var req = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);

        AnsiConsole.MarkupLine($"\n[green]✓[/] [bold]Access request created[/]");
        AnsiConsole.MarkupLine($"  [grey]Request ID[/]  [cyan]{req!.Id[..8]}...[/]");
        AnsiConsole.MarkupLine($"  [grey]Role[/]        [yellow]{req.RoleName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Cluster[/]     [cyan]{req.ClusterName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Duration[/]    {req.RequestedDuration}");
        AnsiConsole.MarkupLine($"  [grey]Expires[/]     {req.RequestExpiresAt.ToLocalTime():HH:mm:ss}");

        if (!wait) return;

        // Poll for approval.
        AnsiConsole.MarkupLine("\n  [yellow]●[/] Waiting for approval...");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(5000, ct);

            var pollResp = await http.GetAsync($"api/v1/access-requests/{req.Id}", ct);
            if (!pollResp.IsSuccessStatusCode) continue;

            var pollJson = await pollResp.Content.ReadAsStringAsync(ct);
            var updated = JsonSerializer.Deserialize(pollJson, CliJsonContext.Default.AccessRequestResponse);

            if (updated?.Status == "Approved")
            {
                AnsiConsole.MarkupLine($"\n[green]✓[/] [bold]Access approved![/]");
                AnsiConsole.MarkupLine($"  [grey]Approved by[/]  {updated.ReviewerEmail ?? "unknown"}");
                AnsiConsole.MarkupLine($"  [grey]Grant expires[/] {updated.GrantExpiresAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss K}");
                AnsiConsole.MarkupLine($"\n  Run [bold]clustral kube login {clusterName}[/] to connect.");
                return;
            }
            if (updated?.Status is "Denied" or "Expired")
            {
                AnsiConsole.MarkupLine($"\n[red]✗[/] [bold]Access {updated.Status.ToLower()}[/]");
                if (updated.DenialReason is not null)
                    AnsiConsole.MarkupLine($"  [grey]Reason[/]  {updated.DenialReason.EscapeMarkup()}");
                ctx.ExitCode = 1;
                return;
            }
        }
    }

    private static async Task HandleLsAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();
        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var status = ctx.ParseResult.GetValueForOption(StatusFilterOption);
        var active = ctx.ParseResult.GetValueForOption(ActiveOption);
        var qs = "?mine=true";
        if (active) qs += "&active=true";
        else if (!string.IsNullOrEmpty(status)) qs += $"&status={status}";

        var response = await http.GetAsync($"api/v1/access-requests{qs}", ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            CliErrors.WriteHttpError((int)response.StatusCode, detail);
            ctx.ExitCode = 1;
            return;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.AccessRequestListResponse);

        if (result is null || result.Requests.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No access requests found.[/]");
            return;
        }

        RenderAccessTable(AnsiConsole.Console, result.Requests);
    }

    private static async Task HandleApproveAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();

        var requestId = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        if (!ValidationHelper.Validate(AnsiConsole.Console, new AccessActionValidator(),
            new AccessActionInput(requestId), ctx))
            return;

        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var body = new AccessRequestApproveRequest();
        var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestApproveRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"api/v1/access-requests/{requestId}/approve", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            CliErrors.WriteHttpError((int)response.StatusCode, detail);
            ctx.ExitCode = 1;
            return;
        }

        var respJson = await response.Content.ReadAsStringAsync(ct);
        var req = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);

        AnsiConsole.MarkupLine($"[green]✓[/] Approved request [cyan]{req!.Id[..8]}...[/]");
        AnsiConsole.MarkupLine($"  [grey]User[/]     {req.RequesterEmail.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  [grey]Role[/]     [yellow]{req.RoleName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Cluster[/]  [cyan]{req.ClusterName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Expires[/]  {req.GrantExpiresAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss K}");
    }

    private static async Task HandleDenyAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();

        var requestId = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());
        var reason = ctx.ParseResult.GetValueForOption(DenyReasonOption)!;

        if (!ValidationHelper.Validate(AnsiConsole.Console, new AccessDenyValidator(),
            new AccessDenyInput(requestId, reason), ctx))
            return;

        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var body = new AccessRequestDenyRequest { Reason = reason };
        var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestDenyRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"api/v1/access-requests/{requestId}/deny", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            CliErrors.WriteHttpError((int)response.StatusCode, detail);
            ctx.ExitCode = 1;
            return;
        }

        AnsiConsole.MarkupLine($"[red]✗[/] Denied request [cyan]{requestId[..Math.Min(8, requestId.Length)]}...[/]");
        AnsiConsole.MarkupLine($"  [grey]Reason[/]  {reason.EscapeMarkup()}");
    }

    private static async Task HandleRevokeAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();

        var requestId = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        if (!ValidationHelper.Validate(AnsiConsole.Console, new AccessActionValidator(),
            new AccessActionInput(requestId), ctx))
            return;

        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var reason = ctx.ParseResult.GetValueForOption(
            ctx.ParseResult.CommandResult.Command.Options.FirstOrDefault(o => o.Name == "reason") as Option<string?>);

        var body = new AccessRequestRevokeRequest { Reason = reason };
        var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestRevokeRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"api/v1/access-requests/{requestId}/revoke", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            CliErrors.WriteHttpError((int)response.StatusCode, detail);
            ctx.ExitCode = 1;
            return;
        }

        var respJson = await response.Content.ReadAsStringAsync(ct);
        var req = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);

        AnsiConsole.MarkupLine($"[red]✗[/] Revoked grant [cyan]{req!.Id[..8]}...[/]");
        AnsiConsole.MarkupLine($"  [grey]User[/]     {req.RequesterEmail.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  [grey]Role[/]     [yellow]{req.RoleName.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Cluster[/]  [cyan]{req.ClusterName.EscapeMarkup()}[/]");
        if (reason is not null)
            AnsiConsole.MarkupLine($"  [grey]Reason[/]   {reason.EscapeMarkup()}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rendering
    // ─────────────────────────────────────────────────────────────────────────

    internal static void RenderAccessTable(IAnsiConsole console, List<AccessRequestResponse> requests)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn("ID")
            .AddColumn("Role")
            .AddColumn("Cluster")
            .AddColumn("Status")
            .AddColumn("Requester")
            .AddColumn("Created")
            .AddColumn("Expires");

        foreach (var r in requests)
        {
            var statusMarkup = r.Status switch
            {
                "Pending"  => "[yellow]● Pending[/]",
                "Approved" => "[green]● Approved[/]",
                "Denied"   => "[red]● Denied[/]",
                "Revoked"  => "[red]● Revoked[/]",
                _          => "[dim]● Expired[/]",
            };

            var expires = r.Status == "Approved" && r.GrantExpiresAt.HasValue
                ? r.GrantExpiresAt.Value.ToLocalTime().ToString("MM-dd HH:mm")
                : r.RequestExpiresAt.ToLocalTime().ToString("MM-dd HH:mm");

            table.AddRow(
                $"[dim]{r.Id[..8]}[/]",
                $"[yellow]{r.RoleName.EscapeMarkup()}[/]",
                $"[cyan]{r.ClusterName.EscapeMarkup()}[/]",
                statusMarkup,
                r.RequesterEmail.EscapeMarkup(),
                ClustersListCommand.TimeAgo(r.CreatedAt),
                expires);
        }

        console.Write(table);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared HTTP client setup
    // ─────────────────────────────────────────────────────────────────────────

    private static HttpClient? CreateClient(InvocationContext ctx, out int exitCode)
    {
        exitCode = 0;
        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;

        if (string.IsNullOrWhiteSpace(config.ControlPlaneUrl))
        {
            CliErrors.WriteNotConfigured("ControlPlane URL not configured", "clustral login <url>");
            exitCode = 1;
            return null;
        }

        var cache = new TokenCache();
        var token = cache.ReadAsync().GetAwaiter().GetResult();
        if (token is null)
        {
            CliErrors.WriteNotConfigured("Not logged in", "clustral login");
            exitCode = 1;
            return null;
        }

        var handler = insecure
            ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            : new HttpClientHandler();

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.ControlPlaneUrl.TrimEnd('/') + "/"),
        };
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return http;
    }
}
