using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
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
        "--duration", "Requested access duration (e.g. 8H, 30M, 1D). Default: 8H.");

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
        if (duration is not null) duration = Iso8601Duration.Normalize(duration);
        var reviewers   = ctx.ParseResult.GetValueForOption(ReviewerOption);
        var wait        = ctx.ParseResult.GetValueForOption(WaitOption);

        // Validate input before making HTTP calls.
        var input = new AccessRequestInput(roleName, clusterName, duration);
        if (!ValidationHelper.Validate(AnsiConsole.Console, new AccessRequestValidator(), input, ctx))
            return;

        // Resolve role and cluster names (or GUIDs) → IDs.
        var roleId = await NameResolver.ResolveRoleIdAsync(http, roleName, ctx, ct);
        if (roleId is null) return;
        CliDebug.Log($"Resolved role '{roleName}' → {roleId}");

        var clusterId = await NameResolver.ResolveClusterIdAsync(http, clusterName, ctx, ct);
        if (clusterId is null) return;
        CliDebug.Log($"Resolved cluster '{clusterName}' → {clusterId}");

        var body = new AccessRequestCreateRequest
        {
            RoleId = roleId,
            ClusterId = clusterId,
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
            throw new CliHttpErrorException((int)response.StatusCode, detail);
        }

        var respJson = await response.Content.ReadAsStringAsync(ct);
        var req = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);

        CliDebug.Log($"Created access request: {req!.Id}");
        AnsiConsole.MarkupLine($"\n[green]✓[/] [bold]{Messages.Success.AccessRequestCreated}[/]");
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
                AnsiConsole.MarkupLine($"\n[green]✓[/] [bold]{Messages.Success.AccessApproved}[/]");
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

        var result = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.LoadingAccessRequests,
            async innerCt =>
            {
                var response = await http.GetAsync($"api/v1/access-requests{qs}", innerCt);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(innerCt);
                    return ((int?)response.StatusCode, detail, (AccessRequestListResponse?)null);
                }
                var json = await response.Content.ReadAsStringAsync(innerCt);
                var parsed = JsonSerializer.Deserialize(json, CliJsonContext.Default.AccessRequestListResponse);
                return ((int?)null, (string?)null, parsed);
            }, ct);

        if (result.Item1 is int code)
            throw new CliHttpErrorException(code, result.Item2 ?? "");

        if (result.Item3 is null || result.Item3.Requests.Count == 0)
        {
            if (CliOptions.IsJson)
            {
                Console.WriteLine("{\"requests\":[]}");
                return;
            }
            AnsiConsole.MarkupLine("[dim]No access requests found.[/]");
            return;
        }

        if (CliOptions.IsJson)
        {
            var jsonStr = JsonSerializer.Serialize(result.Item3, CliJsonContext.Default.AccessRequestListResponse);
            Console.WriteLine(jsonStr);
            return;
        }

        RenderAccessTable(AnsiConsole.Console, result.Item3.Requests);
    }

    private static async Task HandleApproveAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();

        var requestId = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        if (!ValidationHelper.Validate(AnsiConsole.Console, new AccessActionValidator(),
            new AccessActionInput(requestId), ctx))
            return;

        CliDebug.Log($"Approving request {requestId}");

        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var (statusCode, detail, req) = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.ApprovingRequest,
            async innerCt =>
            {
                var body = new AccessRequestApproveRequest();
                var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestApproveRequest);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await http.PostAsync($"api/v1/access-requests/{requestId}/approve", content, innerCt);

                if (!response.IsSuccessStatusCode)
                {
                    var d = await response.Content.ReadAsStringAsync(innerCt);
                    return ((int?)response.StatusCode, d, (AccessRequestResponse?)null);
                }
                var respJson = await response.Content.ReadAsStringAsync(innerCt);
                var parsed = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);
                return ((int?)null, (string?)null, parsed);
            }, ct);

        if (statusCode is int code)
            throw new CliHttpErrorException(code, detail ?? "");

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

        CliDebug.Log($"Denying request {requestId}");

        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var (statusCode, detail) = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.DenyingRequest,
            async innerCt =>
            {
                var body = new AccessRequestDenyRequest { Reason = reason };
                var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestDenyRequest);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await http.PostAsync($"api/v1/access-requests/{requestId}/deny", content, innerCt);
                if (!response.IsSuccessStatusCode)
                {
                    var d = await response.Content.ReadAsStringAsync(innerCt);
                    return ((int?)response.StatusCode, d);
                }
                return ((int?)null, (string?)null);
            }, ct);

        if (statusCode is int code)
            throw new CliHttpErrorException(code, detail ?? "");

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

        CliDebug.Log($"Revoking grant {requestId}");

        using var http = CreateClient(ctx, out var exitCode);
        if (http is null) { ctx.ExitCode = exitCode; return; }

        var reason = ctx.ParseResult.GetValueForOption(
            ctx.ParseResult.CommandResult.Command.Options.FirstOrDefault(o => o.Name == "reason") as Option<string?>);

        var (statusCode, detail, req) = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.RevokingGrant,
            async innerCt =>
            {
                var body = new AccessRequestRevokeRequest { Reason = reason };
                var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestRevokeRequest);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await http.PostAsync($"api/v1/access-requests/{requestId}/revoke", content, innerCt);

                if (!response.IsSuccessStatusCode)
                {
                    var d = await response.Content.ReadAsStringAsync(innerCt);
                    return ((int?)response.StatusCode, d, (AccessRequestResponse?)null);
                }
                var respJson = await response.Content.ReadAsStringAsync(innerCt);
                var parsed = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);
                return ((int?)null, (string?)null, parsed);
            }, ct);

        if (statusCode is int code)
            throw new CliHttpErrorException(code, detail ?? "");

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
            CliErrors.WriteNotConfigured(Messages.Errors.ControlPlaneNotConfigured, Messages.Hints.RunLoginWithUrl);
            exitCode = 1;
            return null;
        }

        var cache = new TokenCache();
        var token = cache.ReadAsync().GetAwaiter().GetResult();
        if (token is null)
        {
            CliErrors.WriteNotConfigured(Messages.Errors.NotLoggedIn, Messages.Hints.RunLogin);
            exitCode = 1;
            return null;
        }

        var http = CliHttp.CreateClient(config.ControlPlaneUrl, insecure);
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return http;
    }
}
