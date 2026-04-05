using System.CommandLine;
using Clustral.Cli.Commands;

// ── Root command ──────────────────────────────────────────────────────────────

var root = new RootCommand("Clustral CLI — manage access to Kubernetes clusters via the Clustral ControlPlane.");

root.AddCommand(LoginCommand.Build());
root.AddCommand(KubeLoginCommand.BuildKubeCommand());
root.AddCommand(ClustersListCommand.BuildClustersCommand());

return await root.InvokeAsync(args);
