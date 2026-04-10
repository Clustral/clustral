using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral whoami</c>: a quick one-liner showing the current
/// user's email, active profile, and session validity. Entirely local — no
/// network calls, just JWT decoding in memory.
/// </summary>
internal static class WhoamiCommand
{
    public static Command Build()
    {
        var cmd = new Command("whoami", "Show current user, profile, and session validity.");
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct = ctx.GetCancellationToken();
        var profile = ProfileCommand.GetActiveProfile();
        CliDebug.Log($"Active profile: {profile ?? "default"}");

        var cache = new TokenCache(CliConfig.DefaultTokenPath);
        var token = await cache.ReadAsync(ct);

        if (token is null)
        {
            CliDebug.Log("No token found");
        }

        if (token is null)
        {
            if (CliOptions.IsJson)
            {
                Console.WriteLine("{\"loggedIn\":false}");
                return;
            }
            AnsiConsole.MarkupLine($"[grey]○[/] Not logged in{ProfileCommand.GetProfileBadge()}");
            return;
        }

        var (email, expiresAt) = DecodeEmailAndExpiry(token);
        var valid = expiresAt.HasValue && expiresAt.Value > DateTimeOffset.UtcNow;
        CliDebug.Log($"Email: {email}, valid: {valid}, expires: {expiresAt}");

        if (CliOptions.IsJson)
        {
            var profileJson = profile is not null ? $"\"{profile}\"" : "\"default\"";
            var emailJson = email is not null ? $"\"{email}\"" : "null";
            var expiresJson = expiresAt.HasValue ? $"\"{expiresAt.Value:o}\"" : "null";
            Console.WriteLine($"{{\"loggedIn\":true,\"email\":{emailJson},\"profile\":{profileJson},\"valid\":{(valid ? "true" : "false")},\"expiresAt\":{expiresJson}}}");
            return;
        }

        var indicator = valid ? "[green]●[/]" : "[red]●[/]";
        var emailStr = email?.EscapeMarkup() ?? "[dim]unknown[/]";
        var profileStr = ProfileCommand.GetProfileBadge();

        if (valid && expiresAt.HasValue)
        {
            var remaining = expiresAt.Value - DateTimeOffset.UtcNow;
            var validFor = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h{remaining.Minutes}m"
                : $"{(int)remaining.TotalMinutes}m";
            AnsiConsole.MarkupLine($"{indicator} {emailStr} [dim]({validFor} remaining)[/]{profileStr}");
        }
        else
        {
            AnsiConsole.MarkupLine($"{indicator} {emailStr} [red](expired)[/]{profileStr}");
        }
    }

    internal static (string? Email, DateTimeOffset? ExpiresAt) DecodeEmailAndExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return (null, null);
            var payload = parts[1];
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var json = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')));
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? email = null;
            if (root.TryGetProperty("email", out var e)) email = e.GetString();
            else if (root.TryGetProperty("preferred_username", out var p)) email = p.GetString();
            else if (root.TryGetProperty("sub", out var s)) email = s.GetString();

            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("exp", out var exp))
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());

            return (email, expiresAt);
        }
        catch
        {
            return (null, null);
        }
    }
}
