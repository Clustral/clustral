using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class ProfileTableTests(ITestOutputHelper output)
{
    private static string Render(UserProfileResponse profile, string cpUrl = "https://cp.example.com")
    {
        var roles = profile.Assignments
            .Select(a => a.RoleName)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        var clusters = profile.Assignments
            .Select(a => a.ClusterName)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var console = new TestConsole();
        console.Profile.Width = 80;
        LoginCommand.RenderProfileTable(console, profile, cpUrl, roles, clusters);
        return console.Output;
    }

    [Fact]
    public void SingleCluster_SingleRole()
    {
        var profile = new UserProfileResponse
        {
            Email = "admin@clustral.local",
            DisplayName = "Admin User",
            Assignments =
            [
                new() { ClusterName = "talos-lab", RoleName = "read-only", ClusterId = "c1" },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Single Cluster / Single Role ===");
        output.WriteLine(rendered);

        Assert.Contains("admin@clustral.local", rendered);
        Assert.Contains("talos-lab", rendered);
        Assert.Contains("read-only", rendered);
        Assert.Contains("Access", rendered);
    }

    [Fact]
    public void MultipleCluster_MultipleRoles()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            DisplayName = "Alice Johnson",
            Assignments =
            [
                new() { ClusterName = "production-eu",  RoleName = "admin",     ClusterId = "c1" },
                new() { ClusterName = "production-us",  RoleName = "admin",     ClusterId = "c2" },
                new() { ClusterName = "staging",        RoleName = "read-only", ClusterId = "c3" },
                new() { ClusterName = "dev-playground", RoleName = "developer", ClusterId = "c4" },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Multiple Clusters / Multiple Roles ===");
        output.WriteLine(rendered);

        Assert.Contains("production-eu", rendered);
        Assert.Contains("production-us", rendered);
        Assert.Contains("staging", rendered);
        Assert.Contains("dev-playground", rendered);
        Assert.Contains("admin", rendered);
        Assert.Contains("read-only", rendered);
        Assert.Contains("developer", rendered);
        // "Access" label should appear only once
        Assert.Equal(1, rendered.Split("Access").Length - 1);
    }

    [Fact]
    public void NoAssignments()
    {
        var profile = new UserProfileResponse
        {
            Email = "newuser@example.com",
            DisplayName = "New User",
            Assignments = [],
        };

        var rendered = Render(profile);

        output.WriteLine("=== No Assignments ===");
        output.WriteLine(rendered);

        Assert.Contains("(none assigned)", rendered);
        Assert.DoesNotContain("Access", rendered);
    }

    [Fact]
    public void SameCluster_MultipleRoles()
    {
        var profile = new UserProfileResponse
        {
            Email = "bob@example.com",
            DisplayName = "Bob Smith",
            Assignments =
            [
                new() { ClusterName = "production", RoleName = "read-only", ClusterId = "c1" },
                new() { ClusterName = "production", RoleName = "admin",     ClusterId = "c1" },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Same Cluster / Multiple Roles ===");
        output.WriteLine(rendered);

        // Clusters row should list "production" once
        Assert.Contains("production", rendered);
        // Access section should show both assignments
        Assert.Contains("read-only", rendered);
        Assert.Contains("admin", rendered);
    }

    [Fact]
    public void LongClusterNames_Formatting()
    {
        var profile = new UserProfileResponse
        {
            Email = "ops@corp.io",
            DisplayName = "Operations Team Lead",
            Assignments =
            [
                new() { ClusterName = "us-east-1-production-primary",   RoleName = "cluster-admin",  ClusterId = "c1" },
                new() { ClusterName = "eu-west-1-production-secondary", RoleName = "read-only",      ClusterId = "c2" },
                new() { ClusterName = "ap-south-1-staging",             RoleName = "developer",      ClusterId = "c3" },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Long Cluster Names ===");
        output.WriteLine(rendered);

        Assert.Contains("us-east-1-production-primary", rendered);
        Assert.Contains("eu-west-1-production-secondary", rendered);
        Assert.Contains("ap-south-1-staging", rendered);
    }

    // ── JIT Grant Display ───────────────────────────────────────────────────

    [Fact]
    public void ActiveGrantOnly_ShowsJitRemaining()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            DisplayName = "Alice",
            Assignments = [],
            ActiveGrants =
            [
                new() { ClusterName = "production", RoleName = "admin", GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(3).AddMinutes(42) },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Active Grant Only ===");
        output.WriteLine(rendered);

        Assert.Contains("production", rendered);
        Assert.Contains("admin", rendered);
        Assert.Contains("JIT", rendered);
        Assert.Contains("remaining", rendered);
        Assert.Contains("Access", rendered);
    }

    [Fact]
    public void MixedStaticAndJitGrants()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            DisplayName = "Alice",
            Assignments =
            [
                new() { ClusterName = "staging", RoleName = "read-only", ClusterId = "c1" },
            ],
            ActiveGrants =
            [
                new() { ClusterName = "production", RoleName = "admin", GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(4) },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Mixed Static + JIT ===");
        output.WriteLine(rendered);

        Assert.Contains("staging", rendered);
        Assert.Contains("read-only", rendered);
        Assert.Contains("production", rendered);
        Assert.Contains("JIT", rendered);
    }

    [Fact]
    public void MultipleActiveGrants()
    {
        var profile = new UserProfileResponse
        {
            Email = "ops@example.com",
            DisplayName = "Ops Engineer",
            Assignments = [],
            ActiveGrants =
            [
                new() { ClusterName = "production-eu", RoleName = "admin", GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(2) },
                new() { ClusterName = "production-us", RoleName = "read-only", GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(6) },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Multiple Active Grants ===");
        output.WriteLine(rendered);

        Assert.Contains("production-eu", rendered);
        Assert.Contains("production-us", rendered);
        Assert.Contains("JIT", rendered);
        // "Access" label should appear only once.
        Assert.Equal(1, rendered.Split("Access").Length - 1);
    }

    [Fact]
    public void GrantAboutToExpire_ShowsMinutes()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            DisplayName = "Alice",
            Assignments = [],
            ActiveGrants =
            [
                new() { ClusterName = "prod", RoleName = "admin", GrantExpiresAt = DateTimeOffset.UtcNow.AddMinutes(25) },
            ],
        };

        var rendered = Render(profile);

        output.WriteLine("=== Grant About To Expire (<1h) ===");
        output.WriteLine(rendered);

        Assert.Contains("JIT", rendered);
        Assert.Contains("m remaining", rendered);
        // Should NOT show hours.
        Assert.DoesNotContain("h", rendered.Split("JIT")[1].Split("remaining")[0]);
    }

    [Fact]
    public void NoAssignmentsNoGrants_NoAccessSection()
    {
        var profile = new UserProfileResponse
        {
            Email = "new@example.com",
            DisplayName = "New User",
            Assignments = [],
            ActiveGrants = [],
        };

        var rendered = Render(profile);

        output.WriteLine("=== No Assignments, No Grants ===");
        output.WriteLine(rendered);

        Assert.DoesNotContain("Access", rendered);
    }
}
