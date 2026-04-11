using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Sdk.Kubeconfig;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral status</c>: single-command overview of session,
/// connected clusters, active grants, and ControlPlane reachability.
/// Gracefully degrades when the ControlPlane is unreachable — local data
/// (session + kubeconfig) is always shown.
/// </summary>
internal static class StatusCommand
{
    private static readonly Option<bool> InsecureOption = new(
        "--insecure", "Skip TLS verification.");

    public static Command Build()
    {
        var cmd = new Command("status", "Show session, clusters, grants, and ControlPlane health.");
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;

        CliDebug.Log("Collecting status...");

        var data = await CollectAsync(config, insecure, ct);

        if (CliOptions.IsJson)
        {
            var json = JsonSerializer.Serialize(data, CliJsonContext.Default.StatusOutput);
            Console.WriteLine(json);
            return;
        }

        Render(AnsiConsole.Console, data);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Collection — testable, returns a plain DTO.
    // ─────────────────────────────────────────────────────────────────────────

    internal static async Task<StatusOutput> CollectAsync(
        CliConfig config, bool insecure, CancellationToken ct)
    {
        var output = new StatusOutput();

        // ── Session (local) ─────────────────────────────────────────────
        var tokenPath = CliConfig.DefaultTokenPath;
        if (File.Exists(tokenPath))
        {
            try
            {
                var token = File.ReadAllText(tokenPath).Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    var expiry = LoginCommand.DecodeJwtExpiry(token);
                    output.Session = new StatusSession
                    {
                        LoggedIn = true,
                        ExpiresAt = expiry,
                        Valid = expiry.HasValue && expiry.Value > DateTimeOffset.UtcNow,
                    };

                    // Try to get subject from JWT payload.
                    try
                    {
                        var parts = token.Split('.');
                        if (parts.Length >= 2)
                        {
                            var payload = parts[1];
                            payload += new string('=', (4 - payload.Length % 4) % 4);
                            var json = System.Text.Encoding.UTF8.GetString(
                                Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')));
                            var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("email", out var email))
                                output.Session.Email = email.GetString();
                            else if (doc.RootElement.TryGetProperty("preferred_username", out var pref))
                                output.Session.Email = pref.GetString();
                            else if (doc.RootElement.TryGetProperty("sub", out var sub))
                                output.Session.Email = sub.GetString();
                        }
                    }
                    catch { /* best effort */ }
                }
            }
            catch { /* best effort */ }
        }

        // ── Kubeconfig contexts (local) ─────────────────────────────────
        var kubeconfigPath = KubeconfigWriter.DefaultKubeconfigPath();
        var contexts = LogoutCommand.FindClustralContexts(kubeconfigPath);
        output.Clusters = contexts.Select(c => new StatusCluster
        {
            ContextName = c.ContextName,
            HasToken = !string.IsNullOrEmpty(c.Token),
        }).ToList();

        // ── ControlPlane health + grants (remote, optional) ─────────────
        if (!string.IsNullOrWhiteSpace(config.ControlPlaneUrl) &&
            output.Session is { LoggedIn: true, Valid: true })
        {
            try
            {
                var token = File.ReadAllText(tokenPath).Trim();
                using var http = CliHttp.CreateClient(config.ControlPlaneUrl, insecure);
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                // ControlPlane version
                try
                {
                    var configJson = await http.GetStringAsync("api/v1/config", ct);
                    var cpConfig = JsonSerializer.Deserialize(configJson, CliJsonContext.Default.ControlPlaneConfig);
                    output.ControlPlane = new StatusControlPlane
                    {
                        Url = config.ControlPlaneUrl,
                        Online = true,
                        Version = cpConfig?.Version,
                    };
                    CliDebug.Log($"ControlPlane online: v{cpConfig?.Version}");
                }
                catch
                {
                    output.ControlPlane = new StatusControlPlane
                    {
                        Url = config.ControlPlaneUrl,
                        Online = false,
                    };
                    CliDebug.Log("ControlPlane unreachable");
                }

                // Active grants
                try
                {
                    var profileJson = await http.GetStringAsync("api/v1/users/me", ct);
                    var profile = JsonSerializer.Deserialize(profileJson, CliJsonContext.Default.UserProfileResponse);
                    if (profile?.ActiveGrants is { Count: > 0 })
                    {
                        output.Grants = profile.ActiveGrants.Select(g => new StatusGrant
                        {
                            ClusterName = g.ClusterName,
                            RoleName = g.RoleName,
                            ExpiresAt = g.GrantExpiresAt,
                        }).ToList();
                    }
                }
                catch { /* best effort */ }
            }
            catch { /* best effort */ }
        }
        else if (!string.IsNullOrWhiteSpace(config.ControlPlaneUrl))
        {
            output.ControlPlane = new StatusControlPlane
            {
                Url = config.ControlPlaneUrl,
                Online = false,
            };
        }

        return output;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rendering
    // ─────────────────────────────────────────────────────────────────────────

    internal static void Render(IAnsiConsole console, StatusOutput data)
    {
        console.WriteLine();

        // ── Session ─────────────────────────────────────────────────────
        console.MarkupLine($"[bold]Session[/]{ProfileCommand.GetProfileBadge()}");
        var sessionTable = new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn(new TableColumn("Key").PadLeft(2).PadRight(2))
            .AddColumn("Value");

        if (data.Session is { LoggedIn: true })
        {
            sessionTable.AddRow("[grey]Logged in as[/]",
                data.Session.Email?.EscapeMarkup() ?? "[dim](unknown)[/]");

            var accounts = AccountsCommand.ListAccounts();
            if (accounts.Count > 1)
            {
                var activeAccount = AccountsCommand.GetActiveAccount() ?? "(none)";
                sessionTable.AddRow("[grey]Account[/]",
                    $"{activeAccount.EscapeMarkup()} [dim]({accounts.Count} accounts available)[/]");
            }

            if (data.Session.ExpiresAt.HasValue)
            {
                var remaining = data.Session.ExpiresAt.Value - DateTimeOffset.UtcNow;
                var validFor = remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                    : remaining.TotalMinutes > 0
                        ? $"{(int)remaining.TotalMinutes}m"
                        : "expired";
                var color = remaining.TotalMinutes < 10 ? "red"
                          : remaining.TotalHours < 1   ? "yellow"
                          : "green";
                sessionTable.AddRow("[grey]Valid until[/]",
                    $"{data.Session.ExpiresAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss K} [[[{color}]valid for {validFor}[/]]]");
            }
        }
        else
        {
            sessionTable.AddRow("[grey]Status[/]", "[yellow]Not logged in[/]");
        }
        console.Write(sessionTable);
        console.WriteLine();

        // ── Clusters (kubeconfig) ───────────────────────────────────────
        console.MarkupLine("[bold]Clusters[/] [dim](kubeconfig)[/]");
        if (data.Clusters.Count == 0)
        {
            console.MarkupLine("  [dim]No clustral contexts in kubeconfig.[/]");
        }
        else
        {
            foreach (var c in data.Clusters)
            {
                var indicator = c.HasToken ? "[green]●[/]" : "[grey]○[/]";
                console.MarkupLine($"  {indicator} [cyan]{c.ContextName.EscapeMarkup()}[/]");
            }
        }
        console.WriteLine();

        // ── Active grants ───────────────────────────────────────────────
        if (data.Grants.Count > 0)
        {
            console.MarkupLine("[bold]Active Grants[/]");
            foreach (var g in data.Grants)
            {
                var remaining = g.ExpiresAt - DateTimeOffset.UtcNow;
                var validFor = remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                    : $"{(int)remaining.TotalMinutes}m";
                console.MarkupLine(
                    $"  [cyan]{g.ClusterName.EscapeMarkup()}[/] [dim]→[/] [yellow]{g.RoleName.EscapeMarkup()}[/] [dim][[JIT {validFor} remaining]][/]");
            }
            console.WriteLine();
        }

        // ── ControlPlane ────────────────────────────────────────────────
        if (data.ControlPlane is not null)
        {
            console.MarkupLine("[bold]ControlPlane[/]");
            var cpIndicator = data.ControlPlane.Online ? "[green]●[/] Online" : "[red]●[/] Unreachable";
            var version = data.ControlPlane.Version is not null
                ? $" [dim](v{data.ControlPlane.Version.EscapeMarkup()})[/]"
                : "";
            console.MarkupLine($"  {cpIndicator}  {data.ControlPlane.Url.EscapeMarkup()}{version}");
            console.WriteLine();
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class StatusOutput
{
    [JsonPropertyName("session")]      public StatusSession?      Session      { get; set; }
    [JsonPropertyName("clusters")]     public List<StatusCluster> Clusters     { get; set; } = [];
    [JsonPropertyName("grants")]       public List<StatusGrant>   Grants       { get; set; } = [];
    [JsonPropertyName("controlPlane")] public StatusControlPlane? ControlPlane { get; set; }
}

internal sealed class StatusSession
{
    [JsonPropertyName("loggedIn")]  public bool              LoggedIn  { get; set; }
    [JsonPropertyName("email")]     public string?           Email     { get; set; }
    [JsonPropertyName("valid")]     public bool              Valid     { get; set; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset?   ExpiresAt { get; set; }
}

internal sealed class StatusCluster
{
    [JsonPropertyName("contextName")] public string ContextName { get; set; } = string.Empty;
    [JsonPropertyName("hasToken")]    public bool   HasToken    { get; set; }
}

internal sealed class StatusGrant
{
    [JsonPropertyName("clusterName")] public string         ClusterName { get; set; } = string.Empty;
    [JsonPropertyName("roleName")]    public string         RoleName    { get; set; } = string.Empty;
    [JsonPropertyName("expiresAt")]   public DateTimeOffset ExpiresAt   { get; set; }
}

internal sealed class StatusControlPlane
{
    [JsonPropertyName("url")]     public string  Url     { get; set; } = string.Empty;
    [JsonPropertyName("online")]  public bool    Online  { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}
