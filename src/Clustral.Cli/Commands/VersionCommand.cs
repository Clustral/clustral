using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Clustral.Cli.Config;
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

        try
        {
            using var http = new HttpClient
            {
                BaseAddress = new Uri(config.ControlPlaneUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            if (config.InsecureTls)
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                };
                http.Dispose();
                using var insecureHttp = new HttpClient(handler)
                {
                    BaseAddress = new Uri(config.ControlPlaneUrl.TrimEnd('/') + "/"),
                    Timeout = TimeSpan.FromSeconds(5),
                };
                await FetchAndDisplayControlPlaneVersion(insecureHttp);
                return;
            }

            await FetchAndDisplayControlPlaneVersion(http);
        }
        catch
        {
            AnsiConsole.MarkupLine("[grey]ControlPlane[/]    [dim](unreachable)[/]");
        }
    }

    private static async Task FetchAndDisplayControlPlaneVersion(HttpClient http)
    {
        var response = await http.GetAsync("api/v1/config");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var cpConfig = JsonSerializer.Deserialize(json, CliJsonContext.Default.ControlPlaneConfig);
        var cpVersion = cpConfig?.Version;

        if (!string.IsNullOrEmpty(cpVersion))
            AnsiConsole.MarkupLine($"[grey]ControlPlane[/]    [bold cyan]v{cpVersion.EscapeMarkup()}[/]");
        else
            AnsiConsole.MarkupLine("[grey]ControlPlane[/]    [dim](version unknown)[/]");
    }
}
