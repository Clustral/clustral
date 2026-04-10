using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.Cli.Http;
using Clustral.Cli.Ui;
using Spectre.Console;

namespace Clustral.Cli.Commands;

internal static class VersionCommand
{
    internal static string GetVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "dev";

    public static Command Build()
    {
        var cmd = new Command("version", "Show clustral CLI and ControlPlane versions.");
        cmd.SetHandler(HandleAsync);
        return cmd;
    }

    private static async Task HandleAsync()
    {
        var cliVersion = GetVersion();
        AnsiConsole.MarkupLine($"[grey]CLI[/]            [bold cyan]v{cliVersion.EscapeMarkup()}[/]");

        var config = CliConfig.Load();
        if (string.IsNullOrWhiteSpace(config.ControlPlaneUrl))
        {
            AnsiConsole.MarkupLine("[grey]ControlPlane[/]    [dim](not configured — run 'clustral login <url>')[/]");
            return;
        }

        var cpVersion = await CliHttp.RunWithSpinnerAsync(
            Messages.Spinners.FetchingVersion,
            async ct =>
            {
                using var http = CliHttp.CreateClient(config.ControlPlaneUrl, config.InsecureTls);
                var response = await http.GetAsync("api/v1/config", ct);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                var cpConfig = JsonSerializer.Deserialize(json, CliJsonContext.Default.ControlPlaneConfig);
                return cpConfig?.Version;
            });

        if (!string.IsNullOrEmpty(cpVersion))
            AnsiConsole.MarkupLine($"[grey]ControlPlane[/]    [bold cyan]v{cpVersion.EscapeMarkup()}[/]");
        else
            AnsiConsole.MarkupLine("[grey]ControlPlane[/]    [dim](version unknown)[/]");
    }
}
