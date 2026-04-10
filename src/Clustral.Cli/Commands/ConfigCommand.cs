using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Kubeconfig;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// <c>clustral config</c> — show what the CLI knows about itself.
///
/// Local-only by default (fast, offline). Use <c>--remote</c> to also call the
/// ControlPlane for the user profile and version.
/// </summary>
internal static class ConfigCommand
{
    private static readonly Option<bool> JsonOption = new(
        new[] { "--json" },
        description: "Emit machine-readable JSON instead of a formatted table.");

    private static readonly Option<bool> RemoteOption = new(
        new[] { "--remote" },
        description: "Also call the ControlPlane for user profile and server version.");

    public static Command Build()
    {
        var cmd = new Command("config", "Show what the CLI knows about itself.");

        // `clustral config show`
        var show = new Command("show", "Display CLI configuration, files, and session state.");
        show.AddOption(JsonOption);
        show.AddOption(RemoteOption);
        show.SetHandler(HandleShowAsync);
        cmd.AddCommand(show);

        // `clustral config path`
        var path = new Command("path", "Print the file paths the CLI uses.");
        path.SetHandler(HandlePath);
        cmd.AddCommand(path);

        // `clustral config clean`
        var clean = new Command("clean", "Remove all CLI configuration and return to a fresh state.");
        clean.AddOption(new Option<bool>(["--yes", "-y"], "Skip confirmation prompt."));
        clean.AddOption(new Option<bool>("--dry-run", "Show what would be removed without deleting."));
        clean.SetHandler(HandleCleanAsync);
        cmd.AddCommand(clean);

        // `clustral config` (no subcommand) → show
        cmd.SetHandler(HandleShowAsync);

        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static Task HandlePath(InvocationContext _)
    {
        AnsiConsole.WriteLine(CliConfig.DefaultPath);
        AnsiConsole.WriteLine(CliConfig.DefaultTokenPath);
        AnsiConsole.WriteLine(KubeconfigWriter.DefaultKubeconfigPath());
        return Task.CompletedTask;
    }

    private static Task HandleCleanAsync(InvocationContext ctx)
    {
        var yes = ctx.ParseResult.GetValueForOption(
            (Option<bool>)ctx.ParseResult.CommandResult.Command.Options.First(o => o.Name == "yes"));
        var dryRun = ctx.ParseResult.GetValueForOption(
            (Option<bool>)ctx.ParseResult.CommandResult.Command.Options.First(o => o.Name == "dry-run"));

        var clustralDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clustral");

        // ── Inventory what would be cleaned ──────────────────────────────
        var items = new List<(string Label, string? Path, bool Exists)>();

        var configPath = Path.Combine(clustralDir, "config.json");
        items.Add(("Config", configPath, File.Exists(configPath)));

        var tokenPath = Path.Combine(clustralDir, "token");
        items.Add(("Token", tokenPath, File.Exists(tokenPath)));

        var activeProfilePath = Path.Combine(clustralDir, "active-profile");
        items.Add(("Active profile", activeProfilePath, File.Exists(activeProfilePath)));

        var profilesDir = Path.Combine(clustralDir, "profiles");
        var profiles = ProfileCommand.ListProfiles().Where(p => p != "default").ToList();
        var hasProfiles = Directory.Exists(profilesDir) && profiles.Count > 0;

        var kubeconfigPath = KubeconfigWriter.DefaultKubeconfigPath();
        var clustralContexts = LogoutCommand.FindClustralContexts(kubeconfigPath);

        // ── Display what will be removed ─────────────────────────────────
        var header = dryRun ? "Would remove:" : "This will remove all Clustral CLI configuration:";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{header}[/]");
        AnsiConsole.WriteLine();

        foreach (var (label, path, exists) in items)
        {
            if (exists)
                AnsiConsole.MarkupLine($"  [red]✗[/] {ShortenHome(path!).EscapeMarkup()}");
            else
                AnsiConsole.MarkupLine($"  [dim]– {ShortenHome(path!).EscapeMarkup()}  (does not exist)[/]");
        }

        if (hasProfiles)
            AnsiConsole.MarkupLine($"  [red]✗[/] {ShortenHome(profilesDir).EscapeMarkup()}/  [dim]({profiles.Count} profiles: {string.Join(", ", profiles)})[/]");
        else
            AnsiConsole.MarkupLine($"  [dim]– {ShortenHome(profilesDir).EscapeMarkup()}/  (no profiles)[/]");

        if (clustralContexts.Count > 0)
            AnsiConsole.MarkupLine($"  [red]✗[/] {clustralContexts.Count} clustral-* kubeconfig context(s)");
        else
            AnsiConsole.MarkupLine($"  [dim]– No clustral-* kubeconfig contexts[/]");

        AnsiConsole.WriteLine();

        if (dryRun)
        {
            AnsiConsole.MarkupLine("[dim]No changes made (dry run).[/]");
            return Task.CompletedTask;
        }

        // ── Confirm ──────────────────────────────────────────────────────
        if (!yes)
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                AnsiConsole.MarkupLine("[yellow]![/] Non-interactive session. Use --yes to confirm.");
                ctx.ExitCode = 1;
                return Task.CompletedTask;
            }

            var confirm = AnsiConsole.Prompt(
                new TextPrompt<bool>("Are you sure?")
                    .AddChoice(true).AddChoice(false)
                    .DefaultValue(false)
                    .WithConverter(v => v ? "y" : "n")
                    .PromptStyle(Style.Plain));
            if (!confirm)
            {
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                return Task.CompletedTask;
            }
        }

        // ── Clean ────────────────────────────────────────────────────────
        // 1. Remove kubeconfig contexts first (while we still have the list).
        var writer = new KubeconfigWriter(kubeconfigPath);
        foreach (var (contextName, _) in clustralContexts)
            writer.RemoveClusterEntry(contextName);

        // 2. Delete ~/.clustral/ files.
        if (File.Exists(configPath))  File.Delete(configPath);
        if (File.Exists(tokenPath))   File.Delete(tokenPath);
        if (File.Exists(activeProfilePath)) File.Delete(activeProfilePath);

        // 3. Delete all profiles.
        if (Directory.Exists(profilesDir))
            Directory.Delete(profilesDir, recursive: true);

        AnsiConsole.MarkupLine($"[green]✓[/] [bold]{Messages.Success.Cleaned}[/]");

        return Task.CompletedTask;
    }

    private static async Task HandleShowAsync(InvocationContext ctx)
    {
        var json = ctx.ParseResult.GetValueForOption(JsonOption);
        // --remote not yet wired (placeholder for future ControlPlane call)
        _ = ctx.ParseResult.GetValueForOption(RemoteOption);

        CliDebug.Log($"Config path: {CliConfig.DefaultPath}");
        CliDebug.Log($"Token path: {CliConfig.DefaultTokenPath}");
        var data = await CollectAsync();

        if (json)
        {
            var output = JsonSerializer.Serialize(data, CliJsonContext.Default.ConfigShowOutput);
            AnsiConsole.WriteLine(output);
            return;
        }

        Render(AnsiConsole.Console, data);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Collection (testable — pure function over file paths)
    // ─────────────────────────────────────────────────────────────────────────

    internal static Task<ConfigShowOutput> CollectAsync(
        string? configPath = null,
        string? tokenPath = null,
        string? kubeconfigPath = null)
    {
        configPath     ??= CliConfig.DefaultPath;
        tokenPath      ??= CliConfig.DefaultTokenPath;
        kubeconfigPath ??= KubeconfigWriter.DefaultKubeconfigPath();

        var config = CliConfig.LoadFrom(configPath);

        var output = new ConfigShowOutput
        {
            Files = new ConfigFiles
            {
                Config     = FileInfoFor(configPath),
                Token      = FileInfoFor(tokenPath),
                Kubeconfig = KubeconfigInfoFor(kubeconfigPath),
            },
            ControlPlane = new ConfigControlPlane
            {
                Url           = config.ControlPlaneUrl,
                OidcAuthority = config.OidcAuthority,
                OidcClientId  = config.OidcClientId,
                OidcScopes    = config.OidcScopes,
                InsecureTls   = config.InsecureTls,
                CallbackPort  = config.CallbackPort,
            },
            Session = SessionInfoFor(tokenPath),
            Cli = new ConfigCli
            {
                Version = "v" + VersionCommand.GetVersion(),
            },
        };

        return Task.FromResult(output);
    }

    private static ConfigFileInfo FileInfoFor(string path)
    {
        var info = new ConfigFileInfo { Path = path, Exists = File.Exists(path) };
        if (info.Exists)
        {
            try { info.SizeBytes = new FileInfo(path).Length; }
            catch { /* best effort */ }
        }
        return info;
    }

    private static ConfigKubeconfigInfo KubeconfigInfoFor(string path)
    {
        var info = new ConfigKubeconfigInfo { Path = path, Exists = File.Exists(path) };
        if (!info.Exists) return info;

        try
        {
            var writer = new KubeconfigWriter(path);
            var contexts = writer.ListContextNames();
            info.TotalContexts = contexts.Count;
            info.CurrentContext = writer.GetCurrentContext();
            info.ClustralContexts = contexts
                .Where(c => c.StartsWith("clustral-", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch { /* best effort */ }

        return info;
    }

    private static ConfigSession SessionInfoFor(string tokenPath)
    {
        if (!File.Exists(tokenPath))
        {
            return new ConfigSession { Status = "NotLoggedIn" };
        }

        string token;
        try { token = File.ReadAllText(tokenPath).Trim(); }
        catch { return new ConfigSession { Status = "Unreadable" }; }

        if (string.IsNullOrEmpty(token))
        {
            return new ConfigSession { Status = "NotLoggedIn" };
        }

        var (subject, issuedAt, expiresAt) = DecodeJwtClaims(token);

        var status = "LoggedIn";
        long? validForSeconds = null;
        if (expiresAt is not null)
        {
            var remaining = expiresAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                status = "Expired";
                validForSeconds = 0;
            }
            else
            {
                validForSeconds = (long)remaining.TotalSeconds;
            }
        }

        return new ConfigSession
        {
            Status = status,
            Subject = subject,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            ValidForSeconds = validForSeconds,
        };
    }

    private static (string? Subject, DateTimeOffset? IssuedAt, DateTimeOffset? ExpiresAt) DecodeJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return (null, null, null);

            var payload = parts[1];
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var json = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? subject = null;
            if (root.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String)
                subject = email.GetString();
            else if (root.TryGetProperty("preferred_username", out var pu) && pu.ValueKind == JsonValueKind.String)
                subject = pu.GetString();
            else if (root.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String)
                subject = sub.GetString();

            DateTimeOffset? issuedAt = null;
            if (root.TryGetProperty("iat", out var iat) && iat.ValueKind == JsonValueKind.Number)
                issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat.GetInt64());

            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());

            return (subject, issuedAt, expiresAt);
        }
        catch
        {
            return (null, null, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Render (testable via Spectre.Console.Testing.TestConsole)
    // ─────────────────────────────────────────────────────────────────────────

    internal static void Render(IAnsiConsole console, ConfigShowOutput data)
    {
        // Status line at the top — matches LoginCommand style.
        var statusLine = data.Session.Status switch
        {
            "LoggedIn"  => $"[green]✓[/] [bold]Configured[/]{ProfileCommand.GetProfileBadge()}",
            "Expired"   => $"[yellow]![/] [bold]Session expired[/]{ProfileCommand.GetProfileBadge()}",
            "NotLoggedIn" => $"[grey]○[/] [bold]Not logged in[/]{ProfileCommand.GetProfileBadge()}",
            _           => $"[red]✗[/] [bold]Token unreadable[/]{ProfileCommand.GetProfileBadge()}",
        };
        console.MarkupLine(statusLine);

        // ── Profiles ─────────────────────────────────────────────────────────
        var activeProfile = ProfileCommand.GetActiveProfile() ?? "default";
        var allProfiles = ProfileCommand.ListProfiles();
        console.WriteLine();
        console.MarkupLine("[bold]Profiles[/]");
        foreach (var profile in allProfiles)
        {
            var isActive = profile == activeProfile;
            var indicator = isActive ? "[green]●[/]" : "[grey]○[/]";
            var label = isActive ? $"[bold]{profile.EscapeMarkup()}[/]" : profile.EscapeMarkup();
            console.MarkupLine($"  {indicator} {label}");
        }

        // ── Control plane ────────────────────────────────────────────────────
        RenderSection(console, "Control plane", section =>
        {
            section.AddRow("[grey]URL[/]", string.IsNullOrEmpty(data.ControlPlane.Url)
                ? "[dim](not configured)[/]"
                : $"[cyan]{data.ControlPlane.Url.EscapeMarkup()}[/]");
            section.AddRow("[grey]OIDC issuer[/]", string.IsNullOrEmpty(data.ControlPlane.OidcAuthority)
                ? "[dim](not configured)[/]"
                : data.ControlPlane.OidcAuthority.EscapeMarkup());
            section.AddRow("[grey]OIDC client[/]", data.ControlPlane.OidcClientId.EscapeMarkup());
            section.AddRow("[grey]OIDC scopes[/]", data.ControlPlane.OidcScopes.EscapeMarkup());
            section.AddRow("[grey]Callback port[/]", data.ControlPlane.CallbackPort.ToString());
            section.AddRow("[grey]Insecure TLS[/]", data.ControlPlane.InsecureTls
                ? "[red]true[/]"
                : "[green]false[/]");
        });

        // ── Session ──────────────────────────────────────────────────────────
        RenderSection(console, "Session", section =>
        {
            switch (data.Session.Status)
            {
                case "LoggedIn":
                    section.AddRow("[grey]Status[/]", "[green]Logged in[/]");
                    if (data.Session.Subject is not null)
                        section.AddRow("[grey]Subject[/]", $"[bold]{data.Session.Subject.EscapeMarkup()}[/]");
                    if (data.Session.ExpiresAt is not null)
                    {
                        var remaining = data.Session.ExpiresAt.Value - DateTimeOffset.UtcNow;
                        var validFor = remaining.TotalHours >= 1
                            ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                            : $"{(int)remaining.TotalMinutes}m";
                        var color = remaining.TotalMinutes < 10 ? "red"
                                  : remaining.TotalHours < 1 ? "yellow"
                                  : "green";
                        section.AddRow("[grey]Valid until[/]",
                            $"{data.Session.ExpiresAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss K} [[[{color}]valid for {validFor}[/]]]");
                    }
                    break;

                case "Expired":
                    section.AddRow("[grey]Status[/]", "[red]Expired[/]");
                    if (data.Session.Subject is not null)
                        section.AddRow("[grey]Subject[/]", data.Session.Subject.EscapeMarkup());
                    section.AddRow("[grey]Hint[/]", "[dim]Run [cyan]clustral login[/] to refresh[/]");
                    break;

                case "NotLoggedIn":
                    section.AddRow("[grey]Status[/]", "[yellow]Not logged in[/]");
                    section.AddRow("[grey]Hint[/]", "[dim]Run [cyan]clustral login <controlplane-url>[/][/]");
                    break;

                default: // Unreadable
                    section.AddRow("[grey]Status[/]", "[red]Token file unreadable[/]");
                    break;
            }
        });

        // ── Kubernetes ───────────────────────────────────────────────────────
        RenderSection(console, "Kubernetes", section =>
        {
            section.AddRow("[grey]Kubeconfig[/]", FormatKubeconfigLine(data.Files.Kubeconfig));

            if (!data.Files.Kubeconfig.Exists)
                return;

            section.AddRow("[grey]Current context[/]",
                string.IsNullOrEmpty(data.Files.Kubeconfig.CurrentContext)
                    ? "[dim](none)[/]"
                    : $"[cyan]{data.Files.Kubeconfig.CurrentContext.EscapeMarkup()}[/]");

            section.AddRow("[grey]Clustral contexts[/]",
                data.Files.Kubeconfig.ClustralContexts.Count > 0
                    ? $"[cyan]{string.Join(", ", data.Files.Kubeconfig.ClustralContexts).EscapeMarkup()}[/] [dim]({data.Files.Kubeconfig.ClustralContexts.Count} of {data.Files.Kubeconfig.TotalContexts} total)[/]"
                    : $"[dim](none of {data.Files.Kubeconfig.TotalContexts} total)[/]");
        });

        // ── Files ────────────────────────────────────────────────────────────
        RenderSection(console, "Files", section =>
        {
            var clustralDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clustral");

            foreach (var profile in allProfiles)
            {
                section.AddRow(profile.EscapeMarkup(), "");

                string configPath, tokenPath;
                if (profile == "default")
                {
                    configPath = Path.Combine(clustralDir, "config.json");
                    tokenPath = Path.Combine(clustralDir, "token");
                }
                else
                {
                    var dir = ProfileCommand.GetProfileDir(profile);
                    configPath = Path.Combine(dir, "config.json");
                    tokenPath = Path.Combine(dir, "token");
                }

                section.AddRow("  [grey]Config[/]", FormatFileLine(new ConfigFileInfo
                {
                    Path = configPath,
                    Exists = File.Exists(configPath),
                    SizeBytes = File.Exists(configPath) ? new FileInfo(configPath).Length : 0,
                }));
                section.AddRow("  [grey]Token[/]", FormatFileLine(new ConfigFileInfo
                {
                    Path = tokenPath,
                    Exists = File.Exists(tokenPath),
                    SizeBytes = File.Exists(tokenPath) ? new FileInfo(tokenPath).Length : 0,
                }));
            }
        });

        // ── CLI ──────────────────────────────────────────────────────────────
        RenderSection(console, "CLI", section =>
        {
            section.AddRow("[grey]Version[/]", $"[cyan]{data.Cli.Version.EscapeMarkup()}[/]");
        });
    }

    /// <summary>
    /// Renders a section: blank line, bold header, then a borderless 2-column
    /// table indented by 2 spaces.
    /// </summary>
    private static void RenderSection(IAnsiConsole console, string title, Action<Table> populate)
    {
        console.WriteLine();
        console.MarkupLine($"[bold]{title.EscapeMarkup()}[/]");

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Key").PadLeft(2).PadRight(2))
            .AddColumn(new TableColumn("Value"));

        populate(table);
        console.Write(table);
    }

    private static string FormatFileLine(ConfigFileInfo info)
    {
        var path = ShortenHome(info.Path).EscapeMarkup();
        if (!info.Exists)
            return $"{path}  [dim](does not exist)[/]";
        return $"{path}  [dim]({FormatBytes(info.SizeBytes)})[/]";
    }

    private static string FormatKubeconfigLine(ConfigKubeconfigInfo info)
    {
        var path = ShortenHome(info.Path).EscapeMarkup();
        if (!info.Exists)
            return $"{path}  [dim](does not exist)[/]";
        return $"{path}  [dim]({info.TotalContexts} contexts)[/]";
    }

    private static string ShortenHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal))
            return "~" + path[home.Length..];
        return path;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
