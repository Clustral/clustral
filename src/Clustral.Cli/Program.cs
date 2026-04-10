using System.CommandLine;
using Clustral.Cli;
using Clustral.Cli.Commands;
using Clustral.Cli.Ui;
using Spectre.Console;

// ── Root command ──────────────────────────────────────────────────────────────

var root = new RootCommand("Clustral CLI — Kubernetes access proxy");

var debugOption = new Option<bool>("--debug",
    "Enable verbose debug output including HTTP traces and exception details.");
root.AddGlobalOption(debugOption);

var outputOption = new Option<string>("--output", () => "table",
    "Output format: table (default) or json.");
outputOption.AddAlias("-o");
root.AddGlobalOption(outputOption);

var noColorOption = new Option<bool>("--no-color",
    "Disable colored output (for CI/CD pipelines and log files).");
root.AddGlobalOption(noColorOption);

root.AddCommand(LoginCommand.Build());
root.AddCommand(LogoutCommand.Build());
root.AddCommand(KubeLoginCommand.BuildKubeCommand());
root.AddCommand(ClustersListCommand.BuildClustersCommand());
root.AddCommand(UsersCommand.BuildUsersCommand());
root.AddCommand(RolesCommand.BuildRolesCommand());
root.AddCommand(AccessCommand.BuildAccessCommand());
root.AddCommand(ConfigCommand.Build());
root.AddCommand(StatusCommand.Build());
root.AddCommand(UpdateCommand.Build());
root.AddCommand(VersionCommand.Build());

// ── Set debug flag before invoking ───────────────────────────────────────────
// Read from the parsed result so `--debug` works regardless of position.

var parseResult = root.Parse(args);
CliDebug.Enabled = parseResult.GetValueForOption(debugOption);
CliOptions.OutputFormat = parseResult.GetValueForOption(outputOption) ?? "table";

if (parseResult.GetValueForOption(noColorOption) ||
    Environment.GetEnvironmentVariable("NO_COLOR") is not null)
{
    AnsiConsole.Profile.Capabilities.Ansi = false;
}

// ── Global exception handler — single catch for the entire CLI ───────────────

try
{
    return await root.InvokeAsync(args);
}
catch (Exception ex)
{
    return CliExceptionHandler.Handle(ex);
}
