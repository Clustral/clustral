using System.CommandLine;
using Clustral.Cli.Commands;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

/// <summary>
/// Verifies the System.CommandLine tree is wired up correctly —
/// all commands, subcommands, options, and arguments are present.
/// </summary>
public class CommandTreeTests(ITestOutputHelper output)
{
    private void DumpCommand(Command cmd, int indent = 0)
    {
        var prefix = new string(' ', indent * 2);
        output.WriteLine($"{prefix}{cmd.Name} — \"{cmd.Description}\"");
        foreach (var arg in cmd.Arguments)
            output.WriteLine($"{prefix}  <{arg.Name}> ({arg.ValueType.Name})");
        foreach (var opt in cmd.Options)
            output.WriteLine($"{prefix}  --{opt.Name}");
        foreach (var sub in cmd.Subcommands)
            DumpCommand(sub, indent + 1);
    }

    [Fact]
    public void LoginCommand_HasExpectedOptions()
    {
        var cmd = LoginCommand.Build();
        DumpCommand(cmd);

        Assert.Equal("login", cmd.Name);
        Assert.Contains(cmd.Options, o => o.Name == "port");
        Assert.Contains(cmd.Options, o => o.Name == "insecure");
        Assert.Contains(cmd.Options, o => o.Name == "force");
        Assert.Single(cmd.Arguments);
    }

    [Fact]
    public void LogoutCommand_HasExpectedOptions()
    {
        var cmd = LogoutCommand.Build();
        DumpCommand(cmd);

        Assert.Equal("logout", cmd.Name);
        Assert.Contains(cmd.Options, o => o.Name == "insecure");
    }

    [Fact]
    public void KubeCommand_HasLoginAndLsSubcommands()
    {
        var kube = KubeLoginCommand.BuildKubeCommand();
        DumpCommand(kube);

        Assert.Equal("kube", kube.Name);

        var subcommands = kube.Subcommands.Select(s => s.Name).ToList();
        Assert.Contains("login", subcommands);
        Assert.Contains("logout", subcommands);
        Assert.Contains("ls", subcommands);
    }

    [Fact]
    public void KubeLoginSubcommand_HasExpectedOptionsAndArgs()
    {
        var kube = KubeLoginCommand.BuildKubeCommand();
        var login = kube.Subcommands.First(s => s.Name == "login");
        DumpCommand(login);

        Assert.Single(login.Arguments);
        Assert.Contains(login.Options, o => o.Name == "context-name");
        Assert.Contains(login.Options, o => o.Name == "ttl");
        Assert.Contains(login.Options, o => o.Name == "no-set-context");
        Assert.Contains(login.Options, o => o.Name == "insecure");
    }

    [Fact]
    public void ClustersCommand_HasListSubcommand()
    {
        var clusters = ClustersListCommand.BuildClustersCommand();
        DumpCommand(clusters);

        Assert.Equal("clusters", clusters.Name);

        var list = clusters.Subcommands.FirstOrDefault(s => s.Name == "list");
        Assert.NotNull(list);
        Assert.Contains(list!.Aliases, a => a == "ls");
        Assert.Contains(list.Options, o => o.Name == "status");
        Assert.Contains(list.Options, o => o.Name == "insecure");
    }

    [Fact]
    public void VersionCommand_HasNoOptionsOrArgs()
    {
        var cmd = VersionCommand.Build();
        DumpCommand(cmd);

        Assert.Equal("version", cmd.Name);
        Assert.Empty(cmd.Arguments);
    }

    [Fact]
    public void UpdateCommand_HasExpectedOptions()
    {
        var cmd = UpdateCommand.Build();
        DumpCommand(cmd);

        Assert.Equal("update", cmd.Name);
        Assert.Contains(cmd.Options, o => o.Name == "pre");
        Assert.Contains(cmd.Options, o => o.Name == "check");
    }
}
