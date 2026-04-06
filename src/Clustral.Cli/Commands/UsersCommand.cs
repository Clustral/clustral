using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Sdk.Auth;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral users ls</c>: lists all users for reviewer discovery.
/// </summary>
internal static class UsersCommand
{
    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification.");

    public static Command BuildUsersCommand()
    {
        var users = new Command("users", "Manage users.");
        users.AddCommand(BuildLsSubcommand());
        return users;
    }

    private static Command BuildLsSubcommand()
    {
        var cmd = new Command("ls", "List all users.");
        cmd.AddOption(InsecureOption);
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync(InvocationContext ctx)
    {
        var ct       = ctx.GetCancellationToken();
        var config   = CliConfig.Load();
        var insecure = ctx.ParseResult.GetValueForOption(InsecureOption) || config.InsecureTls;

        var controlPlaneUrl = config.ControlPlaneUrl;
        if (string.IsNullOrWhiteSpace(controlPlaneUrl))
        {
            await Console.Error.WriteLineAsync(
                "error: ControlPlaneUrl not set. Run 'clustral login <url>' first.");
            ctx.ExitCode = 1;
            return;
        }

        var cache = new TokenCache();
        var token = await cache.ReadAsync(ct);
        if (token is null)
        {
            await Console.Error.WriteLineAsync(
                "error: No token found. Run 'clustral login' first.");
            ctx.ExitCode = 1;
            return;
        }

        var handler = insecure
            ? new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }
            : new HttpClientHandler();

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(controlPlaneUrl.TrimEnd('/') + "/"),
        };
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await http.GetAsync("api/v1/users", ct);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                await Console.Error.WriteLineAsync($"error: {(int)response.StatusCode} {detail}");
                ctx.ExitCode = 1;
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserListResponse);

            if (result is null || result.Users.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No users found.[/]");
                return;
            }

            RenderUsersTable(AnsiConsole.Console, result.Users);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            ctx.ExitCode = 1;
        }
    }

    internal static void RenderUsersTable(IAnsiConsole console, List<UserResponse> users)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn("Email")
            .AddColumn("Display Name")
            .AddColumn("Last Seen");

        foreach (var u in users)
        {
            var lastSeen = u.LastSeenAt.HasValue
                ? ClustersListCommand.TimeAgo(u.LastSeenAt.Value)
                : "[dim]-[/]";

            table.AddRow(
                u.Email.EscapeMarkup(),
                u.DisplayName?.EscapeMarkup() ?? "[dim]-[/]",
                lastSeen);
        }

        console.Write(table);
    }
}
