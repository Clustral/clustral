using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class ProfileTableTests(ITestOutputHelper output)
{
    private static string Render(
        UserProfileResponse profile,
        string cpUrl = "https://cp.example.com",
        DateTimeOffset? expiry = null)
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
        LoginCommand.RenderProfileTable(console, profile, cpUrl, roles, clusters, expiry);
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

    // ─────────────────────────────────────────────────────────────────────────
    // JWT "Valid until" row — rendered inside the profile table (Teleport-style).
    // The plain TestConsole strips ANSI escape codes but keeps text content,
    // so the literal "valid for ..." annotation is observable. Color tags
    // (`green`, `yellow`, `red`) become inert markup that doesn't appear in
    // the rendered output, so we assert on the relative duration string and
    // its `XhYm` vs `Ym` shape rather than the color name.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderProfileTable_WithExpiry_RendersValidUntilRow()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            Assignments = [],
        };
        var expiry = DateTimeOffset.UtcNow.AddHours(8).AddMinutes(15);

        var rendered = Render(profile, expiry: expiry);

        output.WriteLine("=== With Expiry (8h15m) ===");
        output.WriteLine(rendered);

        Assert.Contains("Valid until", rendered);
        // Local-time formatted year/month/day must appear.
        Assert.Contains(expiry.ToLocalTime().ToString("yyyy-MM-dd"), rendered);
        Assert.Contains("valid for", rendered);
    }

    [Fact]
    public void RenderProfileTable_WithExpiry_FarInFuture_UsesHourMinuteFormat()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            Assignments = [],
        };
        // ~12h → green branch, "XhYm" duration shape (clock drift means the
        // exact minute count isn't predictable, so just assert on the shape).
        var expiry = DateTimeOffset.UtcNow.AddHours(12);

        var rendered = Render(profile, expiry: expiry);

        output.WriteLine("=== With Expiry (~12h, green) ===");
        output.WriteLine(rendered);

        Assert.Contains("Valid until", rendered);
        // Match `valid for XXhYm` shape, regardless of the exact minute count.
        var match = System.Text.RegularExpressions.Regex.Match(
            rendered, @"valid for (\d+)h(\d+)m");
        Assert.True(match.Success,
            $"expected `valid for XhYm` shape, got: {rendered}");
        Assert.InRange(int.Parse(match.Groups[1].Value), 11, 12);
    }

    [Fact]
    public void RenderProfileTable_WithExpiry_LessThan10Minutes_UsesMinutesOnlyFormat()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            Assignments = [],
        };
        // ~8 minutes → red branch, "Xm" duration shape (no hours).
        // Comfortable buffer above 0 and below 10 to absorb clock drift.
        var expiry = DateTimeOffset.UtcNow.AddMinutes(8);

        var rendered = Render(profile, expiry: expiry);

        output.WriteLine("=== With Expiry (~8m, red) ===");
        output.WriteLine(rendered);

        Assert.Contains("Valid until", rendered);
        // Match `valid for Xm` shape (no hours component) and bound the
        // minute count between 1 and 9 to absorb clock drift.
        var match = System.Text.RegularExpressions.Regex.Match(
            rendered, @"valid for (\d+)m\b");
        Assert.True(match.Success,
            $"expected `valid for Xm` shape, got: {rendered}");
        var minutes = int.Parse(match.Groups[1].Value);
        Assert.InRange(minutes, 1, 9);
        // Should NOT use the "XhYm" format for sub-hour durations.
        Assert.DoesNotContain("valid for 0h", rendered);
    }

    [Fact]
    public void RenderProfileTable_NoExpiry_DoesNotRenderValidUntilRow()
    {
        var profile = new UserProfileResponse
        {
            Email = "alice@example.com",
            Assignments = [],
        };

        var rendered = Render(profile, expiry: null);

        output.WriteLine("=== No Expiry ===");
        output.WriteLine(rendered);

        Assert.DoesNotContain("Valid until", rendered);
        Assert.DoesNotContain("valid for", rendered);
    }
}
