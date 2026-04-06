using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class ClustersListCommandTests(ITestOutputHelper output)
{
    // ── Truncate ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("exactly-ten", 11, "exactly-ten")]
    [InlineData("this-is-too-long", 10, "this-is-t\u2026")]
    [InlineData("ab", 2, "ab")]
    [InlineData("abc", 2, "a\u2026")]
    public void Truncate_WorksCorrectly(string input, int max, string expected)
    {
        var result = ClustersListCommand.Truncate(input, max);

        output.WriteLine($"Truncate(\"{input}\", {max}) => \"{result}\"");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Truncate_EmptyString()
    {
        var result = ClustersListCommand.Truncate("", 10);

        output.WriteLine($"Truncate(\"\", 10) => \"{result}\"");

        Assert.Equal("", result);
    }

    // ── TimeAgo ─────────────────────────────────────────────────────────────

    [Fact]
    public void TimeAgo_Seconds()
    {
        var dt = DateTimeOffset.UtcNow.AddSeconds(-30);
        var result = ClustersListCommand.TimeAgo(dt);

        output.WriteLine($"TimeAgo(30s ago) => \"{result}\"");

        Assert.EndsWith("s ago", result);
    }

    [Fact]
    public void TimeAgo_Minutes()
    {
        var dt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var result = ClustersListCommand.TimeAgo(dt);

        output.WriteLine($"TimeAgo(15m ago) => \"{result}\"");

        Assert.EndsWith("m ago", result);
    }

    [Fact]
    public void TimeAgo_Hours()
    {
        var dt = DateTimeOffset.UtcNow.AddHours(-5);
        var result = ClustersListCommand.TimeAgo(dt);

        output.WriteLine($"TimeAgo(5h ago) => \"{result}\"");

        Assert.EndsWith("h ago", result);
    }

    [Fact]
    public void TimeAgo_Days()
    {
        var dt = DateTimeOffset.UtcNow.AddDays(-3);
        var result = ClustersListCommand.TimeAgo(dt);

        output.WriteLine($"TimeAgo(3d ago) => \"{result}\"");

        Assert.EndsWith("d ago", result);
    }

    [Fact]
    public void TimeAgo_JustNow()
    {
        var dt = DateTimeOffset.UtcNow.AddSeconds(-2);
        var result = ClustersListCommand.TimeAgo(dt);

        output.WriteLine($"TimeAgo(2s ago) => \"{result}\"");

        Assert.Equal("2s ago", result);
    }

    // ── Table rendering ─────────────────────────────────────────────────────

    [Fact]
    public void RenderClusterTable_SingleCluster()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "c1", Name = "production", Status = "Connected",
                KubernetesVersion = "1.30.1",
                LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                Labels = new() { ["env"] = "prod", ["region"] = "eu-west-1" },
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        ClustersListCommand.RenderClusterTable(console, clusters);

        output.WriteLine("=== Single Cluster ===");
        output.WriteLine(console.Output);

        Assert.Contains("production", console.Output);
        Assert.Contains("Connected", console.Output);
        Assert.Contains("1.30.1", console.Output);
        Assert.Contains("env=prod", console.Output);
    }

    [Fact]
    public void RenderClusterTable_MultipleClusters_MixedStatus()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "c1", Name = "production-eu", Status = "Connected",
                KubernetesVersion = "1.30.1",
                LastSeenAt = DateTimeOffset.UtcNow.AddSeconds(-30),
                Labels = new() { ["env"] = "prod" },
            },
            new()
            {
                Id = "c2", Name = "staging", Status = "Pending",
                KubernetesVersion = "1.29.4",
                Labels = new(),
            },
            new()
            {
                Id = "c3", Name = "dev-playground", Status = "Disconnected",
                KubernetesVersion = null,
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-5),
                Labels = new() { ["team"] = "platform" },
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        ClustersListCommand.RenderClusterTable(console, clusters);

        output.WriteLine("=== Multiple Clusters (Mixed Status) ===");
        output.WriteLine(console.Output);

        Assert.Contains("production-eu", console.Output);
        Assert.Contains("staging", console.Output);
        Assert.Contains("dev-playground", console.Output);
        Assert.Contains("Connected", console.Output);
        Assert.Contains("Pending", console.Output);
        Assert.Contains("Disconnected", console.Output);
    }

    [Fact]
    public void RenderClusterTable_LongClusterName_Truncated()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "c1", Name = "this-is-a-very-long-cluster-name-that-exceeds-limit",
                Status = "Connected", KubernetesVersion = "1.30.0",
                Labels = new(),
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        ClustersListCommand.RenderClusterTable(console, clusters);

        output.WriteLine("=== Long Cluster Name (truncated to 24) ===");
        output.WriteLine(console.Output);

        Assert.DoesNotContain("this-is-a-very-long-cluster-name-that-exceeds-limit", console.Output);
    }

    [Fact]
    public void RenderClusterTable_MatchesKubeLsStyle()
    {
        // Same data rendered by both commands should look consistent.
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "prod-01", Name = "production", Status = "Connected",
                KubernetesVersion = "1.30.1",
                LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Labels = new() { ["env"] = "prod" },
            },
            new()
            {
                Id = "dev-01", Name = "development", Status = "Pending",
                KubernetesVersion = "1.29.4",
                Labels = new() { ["env"] = "dev" },
            },
        };

        var clConsole = new TestConsole();
        clConsole.Profile.Width = 120;
        ClustersListCommand.RenderClusterTable(clConsole, clusters);

        var lsConsole = new TestConsole();
        lsConsole.Profile.Width = 120;
        KubeLsCommand.RenderKubeLsTable(lsConsole, clusters, currentContext: null);

        output.WriteLine("=== clusters list ===");
        output.WriteLine(clConsole.Output);
        output.WriteLine("=== kube ls ===");
        output.WriteLine(lsConsole.Output);

        // Both should use borderless table style and show the same columns.
        Assert.Contains("production", clConsole.Output);
        Assert.Contains("production", lsConsole.Output);
        Assert.Contains("env=prod", clConsole.Output);
        Assert.Contains("env=prod", lsConsole.Output);
    }
}
