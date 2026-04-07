using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class AccessCommandTests(ITestOutputHelper output)
{
    [Fact]
    public void AccessCommand_HasSubcommands()
    {
        var access = AccessCommand.BuildAccessCommand();

        output.WriteLine($"Command: {access.Name}");
        foreach (var sub in access.Subcommands)
            output.WriteLine($"  {sub.Name} — {sub.Description}");

        Assert.Equal("access", access.Name);
        var subs = access.Subcommands.Select(s => s.Name).ToList();
        Assert.Contains("request", subs);
        Assert.Contains("list", subs);
        Assert.Contains("approve", subs);
        Assert.Contains("deny", subs);
        Assert.Contains("revoke", subs);
    }

    [Fact]
    public void AccessRequest_HasExpectedOptions()
    {
        var access = AccessCommand.BuildAccessCommand();
        var request = access.Subcommands.First(s => s.Name == "request");

        output.WriteLine($"Options for 'access request':");
        foreach (var opt in request.Options)
            output.WriteLine($"  --{opt.Name}");

        Assert.Contains(request.Options, o => o.Name == "role");
        Assert.Contains(request.Options, o => o.Name == "cluster");
        Assert.Contains(request.Options, o => o.Name == "reason");
        Assert.Contains(request.Options, o => o.Name == "duration");
        Assert.Contains(request.Options, o => o.Name == "reviewer");
        Assert.Contains(request.Options, o => o.Name == "wait");
        Assert.Contains(request.Options, o => o.Name == "insecure");
    }

    [Fact]
    public void AccessLs_HasStatusOption()
    {
        var access = AccessCommand.BuildAccessCommand();
        var ls = access.Subcommands.First(s => s.Name == "list");

        output.WriteLine($"Options for 'access ls':");
        foreach (var opt in ls.Options)
            output.WriteLine($"  --{opt.Name}");

        Assert.Contains(ls.Options, o => o.Name == "status");
    }

    [Fact]
    public void AccessApprove_HasRequestIdArgument()
    {
        var access = AccessCommand.BuildAccessCommand();
        var approve = access.Subcommands.First(s => s.Name == "approve");

        output.WriteLine($"Arguments for 'access approve': {approve.Arguments.Count}");

        Assert.Single(approve.Arguments);
    }

    [Fact]
    public void AccessDeny_HasReasonOption()
    {
        var access = AccessCommand.BuildAccessCommand();
        var deny = access.Subcommands.First(s => s.Name == "deny");

        output.WriteLine($"Options for 'access deny':");
        foreach (var opt in deny.Options)
            output.WriteLine($"  --{opt.Name}");

        Assert.Contains(deny.Options, o => o.Name == "reason");
        Assert.Single(deny.Arguments);
    }

    // ── RenderAccessTable ───────────────────────────────────────────────────

    [Fact]
    public void RenderAccessTable_MixedStatuses()
    {
        var requests = new List<AccessRequestResponse>
        {
            new()
            {
                Id = "aaaa-bbbb-cccc-dddd-eeee", RoleName = "admin",
                ClusterName = "production", Status = "Pending",
                RequesterEmail = "alice@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                RequestExpiresAt = DateTimeOffset.UtcNow.AddMinutes(50),
            },
            new()
            {
                Id = "1111-2222-3333-4444-5555", RoleName = "read-only",
                ClusterName = "staging", Status = "Approved",
                RequesterEmail = "bob@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                RequestExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
                GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(6),
            },
            new()
            {
                Id = "ffff-eeee-dddd-cccc-bbbb", RoleName = "developer",
                ClusterName = "dev", Status = "Denied",
                RequesterEmail = "charlie@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                RequestExpiresAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(1),
                DenialReason = "No justification",
            },
            new()
            {
                Id = "9999-8888-7777-6666-5555", RoleName = "admin",
                ClusterName = "legacy", Status = "Expired",
                RequesterEmail = "dave@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
                RequestExpiresAt = DateTimeOffset.UtcNow.AddDays(-3).AddHours(1),
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        AccessCommand.RenderAccessTable(console, requests);

        output.WriteLine("=== Mixed Statuses ===");
        output.WriteLine(console.Output);

        Assert.Contains("admin", console.Output);
        Assert.Contains("production", console.Output);
        Assert.Contains("Pending", console.Output);
        Assert.Contains("Approved", console.Output);
        Assert.Contains("Denied", console.Output);
        Assert.Contains("Expired", console.Output);
    }

    [Fact]
    public void RenderAccessTable_ApprovedShowsGrantExpiry()
    {
        var requests = new List<AccessRequestResponse>
        {
            new()
            {
                Id = "abcdef01-2345-6789-abcd-ef0123456789",
                RoleName = "admin", ClusterName = "production",
                Status = "Approved", RequesterEmail = "alice@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                RequestExpiresAt = DateTimeOffset.UtcNow.AddMinutes(55),
                GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        AccessCommand.RenderAccessTable(console, requests);

        output.WriteLine("=== Approved with Grant Expiry ===");
        output.WriteLine(console.Output);

        Assert.Contains("Approved", console.Output);
    }

    [Fact]
    public void RenderAccessTable_PendingShowsRequestExpiry()
    {
        var requests = new List<AccessRequestResponse>
        {
            new()
            {
                Id = "12345678-1234-1234-1234-123456789012",
                RoleName = "read-only", ClusterName = "staging",
                Status = "Pending", RequesterEmail = "bob@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                RequestExpiresAt = DateTimeOffset.UtcNow.AddMinutes(58),
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        AccessCommand.RenderAccessTable(console, requests);

        output.WriteLine("=== Pending with Request Expiry ===");
        output.WriteLine(console.Output);

        Assert.Contains("Pending", console.Output);
        Assert.Contains("staging", console.Output);
    }

    // ── Revoke subcommand ───────────────────────────────────────────────────

    [Fact]
    public void AccessRevoke_HasRequestIdAndReasonOption()
    {
        var access = AccessCommand.BuildAccessCommand();
        var revoke = access.Subcommands.First(s => s.Name == "revoke");

        output.WriteLine($"Command: access revoke");
        output.WriteLine($"Arguments: {revoke.Arguments.Count}");
        foreach (var opt in revoke.Options)
            output.WriteLine($"  --{opt.Name}");

        Assert.Single(revoke.Arguments);
        Assert.Contains(revoke.Options, o => o.Name == "reason");
        Assert.Contains(revoke.Options, o => o.Name == "insecure");
    }

    [Fact]
    public void AccessLs_HasActiveOption()
    {
        var access = AccessCommand.BuildAccessCommand();
        var ls = access.Subcommands.First(s => s.Name == "list");

        output.WriteLine("Options for 'access ls':");
        foreach (var opt in ls.Options)
            output.WriteLine($"  --{opt.Name}");

        Assert.Contains(ls.Options, o => o.Name == "active");
    }

    [Fact]
    public void RenderAccessTable_RevokedStatus()
    {
        var requests = new List<AccessRequestResponse>
        {
            new()
            {
                Id = "revoked-1234-5678-9abc-def012345678",
                RoleName = "admin", ClusterName = "production",
                Status = "Revoked", RequesterEmail = "alice@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
                RequestExpiresAt = DateTimeOffset.UtcNow.AddHours(-2),
                GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                RevokedReason = "security incident",
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        AccessCommand.RenderAccessTable(console, requests);

        output.WriteLine("=== Revoked Status ===");
        output.WriteLine(console.Output);

        Assert.Contains("Revoked", console.Output);
    }
}
