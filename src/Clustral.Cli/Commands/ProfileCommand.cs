using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral profile</c> — manage named configuration profiles
/// for switching between environments (dev, staging, prod).
///
/// Profiles are stored as subdirectories under <c>~/.clustral/profiles/</c>.
/// Each profile has its own <c>config.json</c> and <c>token</c> file.
/// The active profile name is stored in <c>~/.clustral/active-profile</c>.
/// When a profile is active, <c>CliConfig.Load()</c> and <c>TokenCache</c>
/// read from the profile directory instead of the default paths.
/// </summary>
internal static class ProfileCommand
{
    public static Command Build()
    {
        var cmd = new Command("profiles", "Manage configuration profiles for multiple environments.");
        cmd.AddCommand(BuildListSubcommand());
        cmd.AddCommand(BuildUseSubcommand());
        cmd.AddCommand(BuildCurrentSubcommand());
        cmd.AddCommand(BuildCreateSubcommand());
        cmd.AddCommand(BuildDeleteSubcommand());
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static Command BuildListSubcommand()
    {
        var cmd = new Command("list", "List available profiles.");
        cmd.AddAlias("ls");
        cmd.SetHandler(HandleListAsync);
        return cmd;
    }

    private static Command BuildUseSubcommand()
    {
        var cmd = new Command("use", "Switch to a named profile.");
        cmd.AddArgument(new Argument<string>("name", "Profile name to activate."));
        cmd.SetHandler(HandleUseAsync);
        return cmd;
    }

    private static Command BuildCurrentSubcommand()
    {
        var cmd = new Command("current", "Show the active profile name.");
        cmd.SetHandler(HandleCurrentAsync);
        return cmd;
    }

    private static Command BuildCreateSubcommand()
    {
        var cmd = new Command("create", "Create a new empty profile.");
        cmd.AddArgument(new Argument<string>("name", "Profile name to create."));
        cmd.SetHandler(HandleCreateAsync);
        return cmd;
    }

    private static Command BuildDeleteSubcommand()
    {
        var cmd = new Command("delete", "Delete a profile.");
        cmd.AddArgument(new Argument<string>("name", "Profile name to delete."));
        cmd.SetHandler(HandleDeleteAsync);
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static Task HandleListAsync(InvocationContext ctx)
    {
        var profiles = ListProfiles();
        var active = GetActiveProfile() ?? "default";

        if (profiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No profiles. Create one with: clustral profile create <name>[/]");

            if (CliOptions.IsJson)
            {
                Console.WriteLine("[]");
            }

            return Task.CompletedTask;
        }

        if (CliOptions.IsJson)
        {
            var items = profiles.Select(p =>
            {
                var cfg = LoadProfileConfig(p);
                return new { name = p, active = p == active, controlPlaneUrl = cfg?.ControlPlaneUrl ?? "" };
            });
            Console.WriteLine(JsonSerializer.Serialize(items));
            return Task.CompletedTask;
        }

        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn("")
            .AddColumn("Profile")
            .AddColumn("ControlPlane URL")
            .AddColumn("Status")
            .AddColumn("Accounts");

        foreach (var profile in profiles)
        {
            var isActive = profile == active;
            var indicator = isActive ? "[green]●[/]" : "[grey]○[/]";
            var name = isActive
                ? $"[bold]{profile.EscapeMarkup()}[/]"
                : profile.EscapeMarkup();

            var cfg = LoadProfileConfig(profile);
            var cpUrl = !string.IsNullOrWhiteSpace(cfg?.ControlPlaneUrl)
                ? $"[dim]{cfg.ControlPlaneUrl.EscapeMarkup()}[/]"
                : "[dim]—[/]";

            var tokenFile = profile == "default"
                ? Path.Combine(ClustralDir, "token")
                : Path.Combine(GetProfileDir(profile), "token");
            var hasToken = File.Exists(tokenFile);
            var status = isActive
                ? "[green]active[/]"
                : hasToken ? "[dim]logged in[/]" : "[dim]—[/]";

            var accountsDir = profile == "default"
                ? Path.Combine(ClustralDir, "accounts")
                : Path.Combine(GetProfileDir(profile), "accounts");
            var accountCount = Directory.Exists(accountsDir)
                ? Directory.GetFiles(accountsDir, "*.token").Length
                : 0;

            table.AddRow(indicator, name, cpUrl, status, accountCount > 0 ? accountCount.ToString() : "[dim]—[/]");
        }

        AnsiConsole.Write(table);

        return Task.CompletedTask;
    }

    private static Task HandleUseAsync(InvocationContext ctx)
    {
        var name = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        CliDebug.Log($"Switching to profile: {name}");

        // "default" clears the active profile → falls back to ~/.clustral/.
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            ClearActiveProfile();
            AnsiConsole.MarkupLine("[green]✓[/] Switched to profile [bold]default[/]");
            return Task.CompletedTask;
        }

        var profileDir = GetProfileDir(name);
        if (!Directory.Exists(profileDir))
        {
            CliErrors.WriteError($"Profile '{name}' does not exist. Create it with: clustral profile create {name}");
            ctx.ExitCode = 1;
            return Task.CompletedTask;
        }

        SetActiveProfile(name);
        AnsiConsole.MarkupLine($"[green]✓[/] Switched to profile [bold]{name.EscapeMarkup()}[/]");

        var cfg = LoadProfileConfig(name);
        if (!string.IsNullOrWhiteSpace(cfg?.ControlPlaneUrl))
            AnsiConsole.MarkupLine($"  [grey]ControlPlane[/]  {cfg.ControlPlaneUrl.EscapeMarkup()}");

        return Task.CompletedTask;
    }

    private static Task HandleCurrentAsync(InvocationContext ctx)
    {
        var active = GetActiveProfile();

        if (CliOptions.IsJson)
        {
            Console.WriteLine(active is not null
                ? $"{{\"profile\":\"{active}\"}}"
                : "{\"profile\":null}");
            return Task.CompletedTask;
        }

        if (active is null)
            AnsiConsole.MarkupLine("[dim]No active profile (using default config).[/]");
        else
            AnsiConsole.MarkupLine($"[green]●[/] [bold]{active.EscapeMarkup()}[/]");

        return Task.CompletedTask;
    }

    private static Task HandleCreateAsync(InvocationContext ctx)
    {
        var name = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        var profileDir = GetProfileDir(name);
        if (Directory.Exists(profileDir))
        {
            CliErrors.WriteError($"Profile '{name}' already exists.");
            ctx.ExitCode = 1;
            return Task.CompletedTask;
        }

        CliDebug.Log($"Creating profile directory: {profileDir}");
        Directory.CreateDirectory(profileDir);

        // Create an empty config.json so the profile is recognized.
        var configPath = Path.Combine(profileDir, "config.json");
        var emptyConfig = new CliConfig();
        var json = JsonSerializer.Serialize(emptyConfig, CliJsonContext.Default.CliConfig);
        File.WriteAllText(configPath, json);

        AnsiConsole.MarkupLine($"[green]✓[/] Created profile [bold]{name.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"  [dim]Login with: clustral profile use {name.EscapeMarkup()} && clustral login <url>[/]");

        return Task.CompletedTask;
    }

    private static Task HandleDeleteAsync(InvocationContext ctx)
    {
        var name = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            CliErrors.WriteError("The default profile cannot be deleted.");
            ctx.ExitCode = 1;
            return Task.CompletedTask;
        }

        var profileDir = GetProfileDir(name);
        if (!Directory.Exists(profileDir))
        {
            CliErrors.WriteError($"Profile '{name}' does not exist.");
            ctx.ExitCode = 1;
            return Task.CompletedTask;
        }

        // If deleting the active profile, clear the active-profile file.
        var active = GetActiveProfile();
        if (active == name)
            ClearActiveProfile();

        CliDebug.Log($"Deleting profile directory: {profileDir}");
        Directory.Delete(profileDir, recursive: true);
        AnsiConsole.MarkupLine($"[red]✗[/] Deleted profile [bold]{name.EscapeMarkup()}[/]");

        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Profile resolution — used by CliConfig.Load() and TokenCache
    // ─────────────────────────────────────────────────────────────────────────

    private static string ClustralDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clustral");

    private static string ProfilesDir => Path.Combine(ClustralDir, "profiles");

    private static string ActiveProfilePath => Path.Combine(ClustralDir, "active-profile");

    internal static string GetProfileDir(string name) => Path.Combine(ProfilesDir, name);

    internal static string? GetActiveProfile()
    {
        if (!File.Exists(ActiveProfilePath)) return null;
        var name = File.ReadAllText(ActiveProfilePath).Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    internal static void SetActiveProfile(string name) =>
        File.WriteAllText(ActiveProfilePath, name);

    internal static void ClearActiveProfile()
    {
        if (File.Exists(ActiveProfilePath))
            File.Delete(ActiveProfilePath);
    }

    internal static List<string> ListProfiles()
    {
        // Always include "default" as the first entry.
        var profiles = new List<string> { "default" };

        if (Directory.Exists(ProfilesDir))
        {
            profiles.AddRange(
                Directory.GetDirectories(ProfilesDir)
                    .Select(Path.GetFileName)
                    .Where(n => n is not null)
                    .Cast<string>()
                    .OrderBy(n => n));
        }

        return profiles;
    }

    internal static CliConfig? LoadProfileConfig(string name)
    {
        if (name == "default")
        {
            var defaultPath = Path.Combine(ClustralDir, "config.json");
            return File.Exists(defaultPath) ? CliConfig.LoadFrom(defaultPath) : null;
        }
        var configPath = Path.Combine(GetProfileDir(name), "config.json");
        return File.Exists(configPath) ? CliConfig.LoadFrom(configPath) : null;
    }

    /// <summary>
    /// Returns a dim Spectre markup badge like <c>[dim](profile: staging)[/]</c>
    /// when a named profile is active, or empty string for the default profile.
    /// Used by success messages across commands.
    /// </summary>
    internal static string GetProfileBadge()
    {
        var active = GetActiveProfile();
        return active is not null ? $" [dim](profile: {active})[/]" : "";
    }

    /// <summary>
    /// Returns the config path for the active profile, or the default path
    /// if no profile is active. Called by <see cref="CliConfig.DefaultPath"/>.
    /// </summary>
    internal static string ResolveConfigPath()
    {
        var active = GetActiveProfile();
        if (active is not null)
        {
            var profileConfig = Path.Combine(GetProfileDir(active), "config.json");
            if (File.Exists(profileConfig))
                return profileConfig;
        }
        return Path.Combine(ClustralDir, "config.json");
    }

    /// <summary>
    /// Returns the token path for the active profile, or the default path
    /// if no profile is active. Called by <see cref="TokenCache.DefaultTokenPath"/>.
    /// </summary>
    internal static string ResolveTokenPath()
    {
        // Check for active account first (multi-account support).
        var accountPath = AccountsCommand.ResolveActiveAccountTokenPath();
        if (accountPath is not null)
            return accountPath;

        // Fall back to legacy single-token path.
        var active = GetActiveProfile();
        if (active is not null)
            return Path.Combine(GetProfileDir(active), "token");
        return Path.Combine(ClustralDir, "token");
    }
}
