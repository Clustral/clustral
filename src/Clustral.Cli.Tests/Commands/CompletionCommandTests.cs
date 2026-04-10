using Clustral.Cli.Commands;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <see cref="CompletionCommand"/> — verifies that each shell
/// script contains all subcommand names and is syntactically plausible.
/// </summary>
public sealed class CompletionCommandTests(ITestOutputHelper output)
{
    private static readonly string[] ExpectedSubcommands =
    [
        "login", "logout", "kube", "clusters", "users", "roles",
        "access", "config", "status", "doctor", "profiles", "accounts", "whoami", "update", "version", "completion",
    ];

    private static readonly string[] ExpectedKubeSubcommands =
        ["login", "logout", "list"];

    private static readonly string[] ExpectedAccessSubcommands =
        ["request", "approve", "deny", "revoke"];

    // ── Bash ────────────────────────────────────────────────────────────────

    [Fact]
    public void BashScript_ContainsAllTopLevelCommands()
    {
        foreach (var cmd in ExpectedSubcommands)
        {
            CompletionCommand.BashScript.Should().Contain(cmd,
                $"bash script should include '{cmd}' subcommand");
        }
    }

    [Fact]
    public void BashScript_ContainsKubeSubcommands()
    {
        foreach (var cmd in ExpectedKubeSubcommands)
            CompletionCommand.BashScript.Should().Contain(cmd);
    }

    [Fact]
    public void BashScript_ContainsAccessSubcommands()
    {
        foreach (var cmd in ExpectedAccessSubcommands)
            CompletionCommand.BashScript.Should().Contain(cmd);
    }

    [Fact]
    public void BashScript_ContainsCompleteFunctionRegistration()
    {
        CompletionCommand.BashScript.Should().Contain("complete -F");
        CompletionCommand.BashScript.Should().Contain("_clustral_completions");
    }

    [Fact]
    public void BashScript_ContainsOutputOption()
    {
        CompletionCommand.BashScript.Should().Contain("--output");
        CompletionCommand.BashScript.Should().Contain("table json");
    }

    // ── Zsh ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ZshScript_ContainsAllTopLevelCommands()
    {
        foreach (var cmd in ExpectedSubcommands)
        {
            CompletionCommand.ZshScript.Should().Contain(cmd,
                $"zsh script should include '{cmd}' subcommand");
        }
    }

    [Fact]
    public void ZshScript_ContainsCompdefRegistration()
    {
        CompletionCommand.ZshScript.Should().Contain("compdef _clustral clustral");
    }

    [Fact]
    public void ZshScript_ContainsDescriptions()
    {
        CompletionCommand.ZshScript.Should().Contain("Sign in via OIDC");
        CompletionCommand.ZshScript.Should().Contain("Diagnose connectivity issues");
    }

    // ── Fish ────────────────────────────────────────────────────────────────

    [Fact]
    public void FishScript_ContainsAllTopLevelCommands()
    {
        foreach (var cmd in ExpectedSubcommands)
        {
            CompletionCommand.FishScript.Should().Contain(cmd,
                $"fish script should include '{cmd}' subcommand");
        }
    }

    [Fact]
    public void FishScript_ContainsCompleteDirectives()
    {
        CompletionCommand.FishScript.Should().Contain("complete -c clustral");
        CompletionCommand.FishScript.Should().Contain("__fish_use_subcommand");
    }

    [Fact]
    public void FishScript_ContainsGlobalOptions()
    {
        // Fish uses `-l name` for long options, not `--name`.
        CompletionCommand.FishScript.Should().Contain("-l debug");
        CompletionCommand.FishScript.Should().Contain("-l no-color");
        CompletionCommand.FishScript.Should().Contain("-l output");
        CompletionCommand.FishScript.Should().Contain("-l insecure");
    }

    // ── All shells contain shell type completions ───────────────────────────

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    public void AllScripts_ContainShellTypeCompletions(string shell)
    {
        var script = shell switch
        {
            "bash" => CompletionCommand.BashScript,
            "zsh"  => CompletionCommand.ZshScript,
            "fish" => CompletionCommand.FishScript,
            _ => throw new ArgumentException(shell),
        };

        script.Should().Contain("bash");
        script.Should().Contain("zsh");
        script.Should().Contain("fish");
    }
}
