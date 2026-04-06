using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http.Headers;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Ui;
using Clustral.Sdk.Auth;
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
            var response = await http.GetAsync("api/v1/roles", ct);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                CliErrors.WriteHttpError((int)response.StatusCode, detail);
                ctx.ExitCode = 1;
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.RoleListResponse);

            if (result is null || result.Roles.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No roles found.[/]");
                return;
            }

            RenderRolesTable(AnsiConsole.Console, result.Roles);
        }
        catch (Exception ex)
        {
            CliErrors.WriteConnectionError(ex);
            ctx.ExitCode = 1;
        }
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
