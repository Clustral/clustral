using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral audit</c>: query audit events from the
/// AuditService REST API with filtering, pagination, and table/JSON output.
/// </summary>
internal static class AuditCommand
{
    private static readonly Option<string?> CategoryOption = new(
        "--category", "Filter by category (access_requests, credentials, clusters, roles, auth, proxy).");

    private static readonly Option<string?> CodeOption = new(
        "--code", "Filter by event code (e.g. CAR002I).");

    private static readonly Option<string?> SeverityOption = new(
        "--severity", "Filter by severity (Info, Warning, Error).");

    private static readonly Option<string?> UserOption = new(
        "--user", "Filter by actor email.");

    private static readonly Option<string?> ClusterOption = new(
        "--cluster", "Filter by cluster ID.");

    private static readonly Option<string?> FromOption = new(
        "--from", "Start of time range (e.g. 2026-04-01).");

    private static readonly Option<string?> ToOption = new(
        "--to", "End of time range (e.g. 2026-04-11).");

    private static readonly Option<int> PageOption = new(
        "--page", () => 1, "Page number (1-based).");

    private static readonly Option<int> PageSizeOption = new(
        "--page-size", () => 50, "Number of events per page (1-200).");

    private static readonly Option<bool> InsecureOption = new(
        "--insecure", "Skip TLS verification.");

    public static Command Build()
    {
        var cmd = new Command("audit", "Query audit events from the Audit Service.");
        cmd.AddAlias("audit-log");
        cmd.AddOption(CategoryOption);
        cmd.AddOption(CodeOption);
        cmd.AddOption(SeverityOption);
        cmd.AddOption(UserOption);
        cmd.AddOption(ClusterOption);
        cmd.AddOption(FromOption);
        cmd.AddOption(ToOption);
        cmd.AddOption(PageOption);
        cmd.AddOption(PageSizeOption);
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;

        var auditUrl = config.AuditServiceUrl;
        if (string.IsNullOrWhiteSpace(auditUrl))
        {
            CliErrors.WriteNotConfigured(
                "Audit Service URL not configured",
                "Run 'clustral login' to auto-discover, or set auditServiceUrl in ~/.clustral/config.json");
            ctx.ExitCode = 1;
            return;
        }

        CliDebug.Log($"Audit Service URL: {auditUrl}");

        // Build query string from filter options.
        var qs = new List<string>();
        var category = ctx.ParseResult.GetValueForOption(CategoryOption);
        var code     = ctx.ParseResult.GetValueForOption(CodeOption);
        var severity = ctx.ParseResult.GetValueForOption(SeverityOption);
        var user     = ctx.ParseResult.GetValueForOption(UserOption);
        var cluster  = ctx.ParseResult.GetValueForOption(ClusterOption);
        var from     = ctx.ParseResult.GetValueForOption(FromOption);
        var to       = ctx.ParseResult.GetValueForOption(ToOption);
        var page     = ctx.ParseResult.GetValueForOption(PageOption);
        var pageSize = ctx.ParseResult.GetValueForOption(PageSizeOption);

        if (category is not null) qs.Add($"category={Uri.EscapeDataString(category)}");
        if (code is not null)     qs.Add($"code={Uri.EscapeDataString(code)}");
        if (severity is not null) qs.Add($"severity={Uri.EscapeDataString(severity)}");
        if (user is not null)     qs.Add($"user={Uri.EscapeDataString(user)}");
        if (cluster is not null)  qs.Add($"clusterId={Uri.EscapeDataString(cluster)}");
        if (from is not null)     qs.Add($"from={Uri.EscapeDataString(from)}");
        if (to is not null)       qs.Add($"to={Uri.EscapeDataString(to)}");
        qs.Add($"page={page}");
        qs.Add($"pageSize={pageSize}");

        var queryString = string.Join("&", qs);
        CliDebug.Log($"Query: api/v1/audit?{queryString}");

        using var http = CliHttp.CreateClient(auditUrl, insecure);

        // Optionally add auth token if available.
        var cache = new TokenCache(CliConfig.DefaultTokenPath);
        var token = await cache.ReadAsync(ct);
        if (token is not null)
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

        var result = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.LoadingAuditEvents,
            async innerCt =>
            {
                var response = await http.GetAsync($"api/v1/audit?{queryString}", innerCt);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(innerCt);
                    throw new CliHttpErrorException((int)response.StatusCode, detail);
                }
                var json = await response.Content.ReadAsStringAsync(innerCt);
                return JsonSerializer.Deserialize(json, CliJsonContext.Default.AuditListResponse);
            }, ct);

        CliDebug.Log($"Fetched {result?.Events.Count ?? 0} event(s), total {result?.TotalCount ?? 0}");

        if (result is null || result.Events.Count == 0)
        {
            if (CliOptions.IsJson)
            {
                Console.WriteLine("{\"events\":[],\"page\":1,\"pageSize\":50,\"totalCount\":0,\"totalPages\":0}");
                return;
            }
            AnsiConsole.MarkupLine("[dim]No audit events found.[/]");
            return;
        }

        if (CliOptions.IsJson)
        {
            var json = JsonSerializer.Serialize(result, CliJsonContext.Default.AuditListResponse);
            Console.WriteLine(json);
            return;
        }

        RenderAuditTable(AnsiConsole.Console, result.Events);

        if (result.TotalPages > 1)
            AnsiConsole.MarkupLine(
                $"\n[dim]Page {result.Page} of {result.TotalPages} ({result.TotalCount} total events)[/]");
    }

    internal static void RenderAuditTable(IAnsiConsole console, List<AuditEventItem> events)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn("Code")
            .AddColumn("Event")
            .AddColumn("User")
            .AddColumn("Cluster")
            .AddColumn("Time")
            .AddColumn("Message");

        foreach (var e in events)
        {
            var severityColor = e.Severity switch
            {
                "Warning" => "yellow",
                "Error"   => "red",
                _         => "cyan",
            };

            var timeAgo = TimeAgo(e.Time);

            table.AddRow(
                $"[{severityColor}]{e.Code.EscapeMarkup()}[/]",
                $"[dim]{e.Event.EscapeMarkup()}[/]",
                e.User?.EscapeMarkup() ?? "[dim]—[/]",
                e.ClusterName?.EscapeMarkup() ?? "[dim]—[/]",
                $"[dim]{timeAgo}[/]",
                Truncate(e.Message ?? "", 60).EscapeMarkup());
        }

        console.Write(table);
    }

    private static string TimeAgo(DateTimeOffset dt)
    {
        var ago = DateTimeOffset.UtcNow - dt;
        if (ago.TotalSeconds < 60) return $"{(int)ago.TotalSeconds}s ago";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24)   return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}

// ── Wire types for AuditService API responses ────────────────────────────────

internal sealed class AuditListResponse
{
    [JsonPropertyName("events")]     public List<AuditEventItem> Events     { get; set; } = [];
    [JsonPropertyName("page")]       public int                  Page       { get; set; }
    [JsonPropertyName("pageSize")]   public int                  PageSize   { get; set; }
    [JsonPropertyName("totalCount")] public long                 TotalCount { get; set; }
    [JsonPropertyName("totalPages")] public int                  TotalPages { get; set; }
}

internal sealed class AuditEventItem
{
    [JsonPropertyName("uid")]          public string  Uid          { get; set; } = string.Empty;
    [JsonPropertyName("event")]        public string  Event        { get; set; } = string.Empty;
    [JsonPropertyName("code")]         public string  Code         { get; set; } = string.Empty;
    [JsonPropertyName("category")]     public string  Category     { get; set; } = string.Empty;
    [JsonPropertyName("severity")]     public string  Severity     { get; set; } = string.Empty;
    [JsonPropertyName("success")]      public bool    Success      { get; set; }
    [JsonPropertyName("user")]         public string? User         { get; set; }
    [JsonPropertyName("resourceType")] public string? ResourceType { get; set; }
    [JsonPropertyName("resourceName")] public string? ResourceName { get; set; }
    [JsonPropertyName("clusterName")]  public string? ClusterName  { get; set; }
    [JsonPropertyName("time")]         public DateTimeOffset Time  { get; set; }
    [JsonPropertyName("message")]      public string? Message      { get; set; }
    [JsonPropertyName("error")]        public string? Error        { get; set; }
}
