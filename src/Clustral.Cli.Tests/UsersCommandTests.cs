using System.CommandLine;
using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class UsersCommandTests(ITestOutputHelper output)
{
    [Fact]
    public void UsersCommand_HasLsSubcommand()
    {
        var users = UsersCommand.BuildUsersCommand();

        output.WriteLine($"Command: {users.Name}");
        foreach (var sub in users.Subcommands)
            output.WriteLine($"  {sub.Name} — {sub.Description}");

        Assert.Equal("users", users.Name);
        Assert.Contains(users.Subcommands, s => s.Name == "ls");
    }

    [Fact]
    public void UsersLs_HasInsecureOption()
    {
        var users = UsersCommand.BuildUsersCommand();
        var ls = users.Subcommands.First(s => s.Name == "ls");

        Assert.Contains(ls.Options, o => o.Name == "insecure");
    }

    [Fact]
    public void RenderUsersTable_MultipleUsers()
    {
        var users = new List<UserResponse>
        {
            new() { Email = "alice@example.com", DisplayName = "Alice Johnson", LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Email = "bob@example.com", DisplayName = "Bob Smith", LastSeenAt = DateTimeOffset.UtcNow.AddHours(-2) },
            new() { Email = "charlie@example.com", DisplayName = null, LastSeenAt = null },
        };

        var console = new TestConsole();
        console.Profile.Width = 100;
        UsersCommand.RenderUsersTable(console, users);

        output.WriteLine("=== Users Table ===");
        output.WriteLine(console.Output);

        Assert.Contains("alice@example.com", console.Output);
        Assert.Contains("Alice Johnson", console.Output);
        Assert.Contains("bob@example.com", console.Output);
        Assert.Contains("charlie@example.com", console.Output);
    }

    [Fact]
    public void RenderUsersTable_MissingDisplayName()
    {
        var users = new List<UserResponse>
        {
            new() { Email = "noname@example.com", DisplayName = null, LastSeenAt = DateTimeOffset.UtcNow },
        };

        var console = new TestConsole();
        console.Profile.Width = 100;
        UsersCommand.RenderUsersTable(console, users);

        output.WriteLine("=== User With No Display Name ===");
        output.WriteLine(console.Output);

        Assert.Contains("noname@example.com", console.Output);
    }

    [Fact]
    public void RenderUsersTable_NeverSeen()
    {
        var users = new List<UserResponse>
        {
            new() { Email = "new@example.com", DisplayName = "New User", LastSeenAt = null },
        };

        var console = new TestConsole();
        console.Profile.Width = 100;
        UsersCommand.RenderUsersTable(console, users);

        output.WriteLine("=== Never Seen User ===");
        output.WriteLine(console.Output);

        Assert.Contains("new@example.com", console.Output);
    }
}
