using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;

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
        var cmd = new Command("version", "Show the clustral CLI version.");
        cmd.SetHandler(() => Console.WriteLine($"{Ui.Ansi.Bold("clustral")} {Ui.Ansi.Cyan($"v{GetVersion()}")}"));
        return cmd;
    }
}
