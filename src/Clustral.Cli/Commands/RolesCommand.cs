using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Auth;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using Spectre.Console;

namespace Clustral.Cli.Commands;

/// <summary>
/// Implements <c>clustral roles ls</c>: lists all roles with their K8s groups.
/// </summary>
internal static class RolesCommand
{
    private static readonly Option<bool> InsecureOption = new(
        "--insecure",
        "Skip TLS verification.");

    public static Command BuildRolesCommand()
    {
        var roles = new Command("roles", "Manage roles.");
        roles.AddCommand(BuildLsSubcommand());
        return roles;
    }

    private static Command BuildLsSubcommand()
    {
        var cmd = new Command("list", "List all roles.");
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
            CliErrors.WriteNotConfigured(Messages.Errors.ControlPlaneNotConfigured, Messages.Hints.RunLoginWithUrl);
            ctx.ExitCode = 1;
            return;
        }

        var token = await SessionHelper.EnsureValidTokenAsync(config, insecure, ct);
        if (token is null)
        {
            CliErrors.WriteNotConfigured(Messages.Errors.NotLoggedIn, Messages.Hints.RunLogin);
            ctx.ExitCode = 1;
            return;
        }
        CliDebug.Log("Loaded config and JWT");

        var result = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.LoadingRoles,
            async innerCt =>
            {
                using var http = CliHttp.CreateClient(controlPlaneUrl, insecure);
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                var response = await http.GetAsync("api/v1/roles", innerCt);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(innerCt);
                    return ((int?)response.StatusCode, detail, (RoleListResponse?)null);
                }

                var json = await response.Content.ReadAsStringAsync(innerCt);
                var parsed = JsonSerializer.Deserialize(json, CliJsonContext.Default.RoleListResponse);
                return ((int?)null, (string?)null, parsed);
            });

        if (result.Item1 is int status)
            throw new CliHttpErrorException(status, result.Item2 ?? "");
        CliDebug.Log($"Fetched {result.Item3?.Roles.Count ?? 0} role(s)");

        if (result.Item3 is null || result.Item3.Roles.Count == 0)
        {
            if (CliOptions.IsJson)
            {
                Console.WriteLine("{\"roles\":[]}");
                return;
            }
            AnsiConsole.MarkupLine("[dim]No roles found.[/]");
            return;
        }

        if (CliOptions.IsJson)
        {
            var jsonStr = JsonSerializer.Serialize(result.Item3, CliJsonContext.Default.RoleListResponse);
            Console.WriteLine(jsonStr);
            return;
        }

        RenderRolesTable(AnsiConsole.Console, result.Item3.Roles);
    }

    internal static void RenderRolesTable(IAnsiConsole console, List<RoleResponse> roles)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn("Role")
            .AddColumn("Description")
            .AddColumn("K8s Groups");

        foreach (var r in roles)
        {
            var groups = r.KubernetesGroups.Count > 0
                ? $"[dim]{string.Join(", ", r.KubernetesGroups).EscapeMarkup()}[/]"
                : "[dim]-[/]";

            table.AddRow(
                $"[yellow]{r.Name.EscapeMarkup()}[/]",
                r.Description.EscapeMarkup(),
                groups);
        }

        console.Write(table);
    }
}
