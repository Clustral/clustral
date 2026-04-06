using System.CommandLine;
using Clustral.Cli.Commands;

// ── Root command ──────────────────────────────────────────────────────────────

var root = new RootCommand("Clustral CLI — Kubernetes access proxy");

root.AddCommand(LoginCommand.Build());
root.AddCommand(LogoutCommand.Build());
root.AddCommand(KubeLoginCommand.BuildKubeCommand());
root.AddCommand(ClustersListCommand.BuildClustersCommand());
root.AddCommand(UsersCommand.BuildUsersCommand());
root.AddCommand(AccessCommand.BuildAccessCommand());
root.AddCommand(UpdateCommand.Build());
root.AddCommand(VersionCommand.Build());

return await root.InvokeAsync(args);
