using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral accounts</c> — manage multiple OIDC accounts
/// within the active profile. Each account is a separate JWT stored in
/// <c>accounts/{email}.token</c>.
/// </summary>
internal static class AccountsCommand
{
    public static Command Build()
    {
        var cmd = new Command("accounts", "Manage logged-in accounts within the current profile.");
        cmd.AddCommand(BuildListSubcommand());
        cmd.AddCommand(BuildUseSubcommand());
        cmd.AddCommand(BuildRemoveSubcommand());
        return cmd;
    }

    private static Command BuildListSubcommand()
    {
        var cmd = new Command("list", "List accounts in the current profile.");
        cmd.AddAlias("ls");
        cmd.SetHandler(HandleListAsync);
        return cmd;
    }

    private static Command BuildUseSubcommand()
    {
        var cmd = new Command("use", "Switch the active account.");
        cmd.AddArgument(new Argument<string>("email", "Account email to activate."));
        cmd.SetHandler(HandleUseAsync);
        return cmd;
    }

    private static Command BuildRemoveSubcommand()
    {
        var cmd = new Command("remove", "Remove a stored account.");
        cmd.AddArgument(new Argument<string>("email", "Account email to remove."));
        cmd.SetHandler(HandleRemoveAsync);
        return cmd;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private static Task HandleListAsync(InvocationContext ctx)
    {
        var accounts = ListAccounts();
        var active = GetActiveAccount();
        CliDebug.Log($"Active account: {active ?? "(none)"}, found {accounts.Count} account(s)");

        if (accounts.Count == 0)
        {
            if (CliOptions.IsJson)
            {
                Console.WriteLine("[]");
                return Task.CompletedTask;
            }
            AnsiConsole.MarkupLine("[dim]No accounts. Run 'clustral login' to add one.[/]");
            return Task.CompletedTask;
        }

        if (CliOptions.IsJson)
        {
            var items = accounts.Select(a =>
            {
                var (_, exp) = WhoamiCommand.DecodeEmailAndExpiry(ReadAccountToken(a) ?? "");
                var valid = exp.HasValue && exp.Value > DateTimeOffset.UtcNow;
                return new { email = a, active = a == active, valid, expiresAt = exp?.ToString("o") };
            });
            Console.WriteLine(JsonSerializer.Serialize(items));
            return Task.CompletedTask;
        }

        foreach (var account in accounts)
        {
            var isActive = account == active;
            var indicator = isActive ? "[green]●[/]" : "[grey]○[/]";
            var label = isActive ? $"[bold]{account.EscapeMarkup()}[/]" : account.EscapeMarkup();

            var token = ReadAccountToken(account);
            var validity = "";
            if (token is not null)
            {
                var (_, exp) = WhoamiCommand.DecodeEmailAndExpiry(token);
                if (exp.HasValue)
                {
                    var remaining = exp.Value - DateTimeOffset.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        var validFor = remaining.TotalHours >= 1
                            ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                            : $"{(int)remaining.TotalMinutes}m";
                        validity = $" [dim]({validFor} remaining)[/]";
                    }
                    else
                    {
                        validity = " [red](expired)[/]";
                    }
                }
            }

            AnsiConsole.MarkupLine($"  {indicator} {label}{validity}");
        }

        return Task.CompletedTask;
    }

    private static Task HandleUseAsync(InvocationContext ctx)
    {
        var email = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        CliDebug.Log($"Switching active account to: {email}");

        var accountPath = GetAccountTokenPath(email);
        if (!File.Exists(accountPath))
        {
            CliErrors.WriteError($"Account '{email}' not found. Run 'clustral login' to add it.");
            ctx.ExitCode = 1;
            return Task.CompletedTask;
        }

        SetActiveAccount(email);
        AnsiConsole.MarkupLine($"[green]✓[/] Switched to account [bold]{email.EscapeMarkup()}[/]{ProfileCommand.GetProfileBadge()}");

        return Task.CompletedTask;
    }

    private static Task HandleRemoveAsync(InvocationContext ctx)
    {
        var email = ctx.ParseResult.GetValueForArgument(
            (Argument<string>)ctx.ParseResult.CommandResult.Command.Arguments.First());

        CliDebug.Log($"Removing account: {email}");

        var accountPath = GetAccountTokenPath(email);
        if (!File.Exists(accountPath))
        {
            CliErrors.WriteError($"Account '{email}' not found.");
            ctx.ExitCode = 1;
            return Task.CompletedTask;
        }

        // If removing the active account, clear it.
        var active = GetActiveAccount();
        if (active == email)
            ClearActiveAccount();

        File.Delete(accountPath);
        AnsiConsole.MarkupLine($"[red]✗[/] Removed account [bold]{email.EscapeMarkup()}[/]");

        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Account resolution — used by ProfileCommand.ResolveTokenPath
    // ─────────────────────────────────────────────────────────────────────────

    private static string ProfileDir
    {
        get
        {
            var active = ProfileCommand.GetActiveProfile();
            return active is not null
                ? ProfileCommand.GetProfileDir(active)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clustral");
        }
    }

    private static string AccountsDir => Path.Combine(ProfileDir, "accounts");

    private static string ActiveAccountPath => Path.Combine(ProfileDir, "active-account");

    internal static string? GetActiveAccount()
    {
        if (!File.Exists(ActiveAccountPath)) return null;
        var name = File.ReadAllText(ActiveAccountPath).Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    internal static void SetActiveAccount(string email) =>
        File.WriteAllText(ActiveAccountPath, email);

    internal static void ClearActiveAccount()
    {
        if (File.Exists(ActiveAccountPath))
            File.Delete(ActiveAccountPath);
    }

    internal static List<string> ListAccounts()
    {
        if (!Directory.Exists(AccountsDir))
            return [];

        return Directory.GetFiles(AccountsDir, "*.token")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n)
            .ToList();
    }

    internal static string GetAccountTokenPath(string email) =>
        Path.Combine(AccountsDir, $"{email}.token");

    internal static string? ReadAccountToken(string email)
    {
        var path = GetAccountTokenPath(email);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllText(path).Trim(); }
        catch { return null; }
    }

    /// <summary>
    /// Stores a JWT under accounts/{email}.token and sets it as the active
    /// account. Creates the accounts/ directory if needed.
    /// Called by LoginCommand after a successful OIDC flow.
    /// </summary>
    internal static void StoreAccountToken(string email, string token)
    {
        Directory.CreateDirectory(AccountsDir);
        var path = GetAccountTokenPath(email);
        File.WriteAllText(path, token);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        SetActiveAccount(email);
        CliDebug.Log($"Stored token for account {email}");
    }

    /// <summary>
    /// Returns the token path for the active account, or null if no account
    /// is active (falls back to legacy single-token path).
    /// </summary>
    internal static string? ResolveActiveAccountTokenPath()
    {
        var active = GetActiveAccount();
        if (active is null) return null;

        var path = GetAccountTokenPath(active);
        return File.Exists(path) ? path : null;
    }
}
