using System.CommandLine;
using Clustral.Cli;
using Clustral.Cli.Commands;
using Clustral.Cli.Ui;

// ── Root command ──────────────────────────────────────────────────────────────

var root = new RootCommand("Clustral CLI — Kubernetes access proxy");

var debugOption = new Option<bool>("--debug",
    "Enable verbose debug output including HTTP traces and exception details.");
root.AddGlobalOption(debugOption);

root.AddCommand(LoginCommand.Build());
root.AddCommand(LogoutCommand.Build());
root.AddCommand(KubeLoginCommand.BuildKubeCommand());
root.AddCommand(ClustersListCommand.BuildClustersCommand());
root.AddCommand(UsersCommand.BuildUsersCommand());
root.AddCommand(RolesCommand.BuildRolesCommand());
root.AddCommand(AccessCommand.BuildAccessCommand());
root.AddCommand(ConfigCommand.Build());
root.AddCommand(UpdateCommand.Build());
root.AddCommand(VersionCommand.Build());

// ── Set debug flag before invoking ───────────────────────────────────────────
// Read from the parsed result so `--debug` works regardless of position.

CliDebug.Enabled = root.Parse(args).GetValueForOption(debugOption);

// ── Global exception handler — single catch for the entire CLI ───────────────

try
{
    return await root.InvokeAsync(args);
}
catch (Exception ex)
{
    return CliExceptionHandler.Handle(ex);
}
