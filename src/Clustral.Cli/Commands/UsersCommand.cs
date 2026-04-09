using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
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
        var cmd = new Command("list", "List all users.");
        cmd.AddAlias("ls");
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
            CliErrors.WriteNotConfigured("ControlPlane URL not configured", "clustral login <url>");
            ctx.ExitCode = 1;
            return;
        }

        var cache = new TokenCache();
        var token = await cache.ReadAsync(ct);
        if (token is null)
        {
            CliErrors.WriteNotConfigured("Not logged in", "clustral login");
            ctx.ExitCode = 1;
            return;
        }

        try
        {
            var result = await CliHttp.RunWithSpinnerAsync(
                "Loading users...",
                async innerCt =>
                {
                    using var http = CliHttp.CreateClient(controlPlaneUrl, insecure);
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);

                    var response = await http.GetAsync("api/v1/users", innerCt);
                    if (!response.IsSuccessStatusCode)
                    {
                        var detail = await response.Content.ReadAsStringAsync(innerCt);
                        return ((int?)response.StatusCode, detail, (UserListResponse?)null);
                    }

                    var json = await response.Content.ReadAsStringAsync(innerCt);
                    var parsed = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserListResponse);
                    return ((int?)null, (string?)null, parsed);
                });

            if (result.Item1 is int status)
            {
                CliErrors.WriteHttpError(status, result.Item2 ?? "");
                ctx.ExitCode = 1;
                return;
            }

            if (result.Item3 is null || result.Item3.Users.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No users found.[/]");
                return;
            }

            RenderUsersTable(AnsiConsole.Console, result.Item3.Users);
        }
        catch (CliHttpTimeoutException)
        {
            CliErrors.WriteError("ControlPlane unreachable (timed out after 5s).");
            ctx.ExitCode = 1;
        }
        catch (Exception ex)
        {
            CliErrors.WriteConnectionError(ex);
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
