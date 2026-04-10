using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Clustral.Cli.Config;
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

        // `clustral config` (no subcommand) → show
        cmd.SetHandler(HandleShowAsync);

        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static Task HandlePath(InvocationContext _)
    {
        AnsiConsole.WriteLine(CliConfig.DefaultPath);
        AnsiConsole.WriteLine(TokenCache.DefaultTokenPath());
        AnsiConsole.WriteLine(KubeconfigWriter.DefaultKubeconfigPath());
        return Task.CompletedTask;
    }

    private static async Task HandleShowAsync(InvocationContext ctx)
    {
        var json = ctx.ParseResult.GetValueForOption(JsonOption);
        // --remote not yet wired (placeholder for future ControlPlane call)
        _ = ctx.ParseResult.GetValueForOption(RemoteOption);

        CliDebug.Log($"Config path: {CliConfig.DefaultPath}");
        CliDebug.Log($"Token path: {TokenCache.DefaultTokenPath()}");
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
        tokenPath      ??= TokenCache.DefaultTokenPath();
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
            "LoggedIn"  => "[green]✓[/] [bold]Configured[/]",
            "Expired"   => "[yellow]![/] [bold]Session expired[/]",
            "NotLoggedIn" => "[grey]○[/] [bold]Not logged in[/]",
            _           => "[red]✗[/] [bold]Token unreadable[/]",
        };
        console.MarkupLine(statusLine);

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
            if (!data.Files.Kubeconfig.Exists)
            {
                section.AddRow("[grey]Status[/]", "[dim](no kubeconfig)[/]");
                return;
            }

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
            section.AddRow("[grey]Config[/]", FormatFileLine(data.Files.Config));
            section.AddRow("[grey]Token[/]", FormatFileLine(data.Files.Token));
            section.AddRow("[grey]Kubeconfig[/]", FormatKubeconfigLine(data.Files.Kubeconfig));
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
