using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class KubeLsCommandTests(ITestOutputHelper output)
{
    [Fact]
    public void RenderKubeLsTable_SingleCluster_NotSelected()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "prod-01", Name = "production", Status = "Connected",
                KubernetesVersion = "1.30.1",
                Labels = new() { ["env"] = "prod", ["region"] = "eu-west-1" },
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        KubeLsCommand.RenderKubeLsTable(console, clusters, currentContext: null);

        output.WriteLine("=== Single Cluster (not selected) ===");
        output.WriteLine(console.Output);

        Assert.Contains("production", console.Output);
        Assert.Contains("prod-01", console.Output);
        Assert.Contains("Connected", console.Output);
        Assert.Contains("1.30.1", console.Output);
        Assert.Contains("env=prod", console.Output);
        Assert.DoesNotContain("\u25b8", console.Output); // no pointer
    }

    [Fact]
    public void RenderKubeLsTable_SelectedCluster_ShowsPointer()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "prod-01", Name = "production", Status = "Connected",
                KubernetesVersion = "1.30.1",
                Labels = new(),
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        KubeLsCommand.RenderKubeLsTable(console, clusters, currentContext: "clustral-prod-01");

        output.WriteLine("=== Selected Cluster (pointer visible) ===");
        output.WriteLine(console.Output);

        // The pointer character should appear.
        Assert.Contains("\u25b8", console.Output);
    }

    [Fact]
    public void RenderKubeLsTable_MultipleClusters_OneSelected()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "dev-01", Name = "development", Status = "Connected",
                KubernetesVersion = "1.29.4",
                Labels = new() { ["env"] = "dev" },
            },
            new()
            {
                Id = "staging-01", Name = "staging", Status = "Pending",
                KubernetesVersion = "1.30.0",
                Labels = new() { ["env"] = "staging" },
            },
            new()
            {
                Id = "prod-01", Name = "production", Status = "Connected",
                KubernetesVersion = "1.30.1",
                Labels = new() { ["env"] = "prod", ["region"] = "eu-west-1" },
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        KubeLsCommand.RenderKubeLsTable(console, clusters, currentContext: "clustral-prod-01");

        output.WriteLine("=== Multiple Clusters (prod selected) ===");
        output.WriteLine(console.Output);

        Assert.Contains("development", console.Output);
        Assert.Contains("staging", console.Output);
        Assert.Contains("production", console.Output);
        Assert.Contains("Connected", console.Output);
        Assert.Contains("Pending", console.Output);
    }

    [Fact]
    public void RenderKubeLsTable_DisconnectedCluster()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "dead-01", Name = "offline-cluster", Status = "Disconnected",
                KubernetesVersion = null,
                Labels = new(),
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        KubeLsCommand.RenderKubeLsTable(console, clusters, currentContext: null);

        output.WriteLine("=== Disconnected Cluster ===");
        output.WriteLine(console.Output);

        Assert.Contains("Disconnected", console.Output);
        Assert.Contains("offline-cluster", console.Output);
    }

    [Fact]
    public void RenderKubeLsTable_WithLabels()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "labeled", Name = "labeled-cluster", Status = "Connected",
                KubernetesVersion = "1.30.0",
                Labels = new()
                {
                    ["env"] = "prod",
                    ["region"] = "us-east-1",
                    ["team"] = "platform",
                },
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 140;
        KubeLsCommand.RenderKubeLsTable(console, clusters, currentContext: null);

        output.WriteLine("=== Cluster With Multiple Labels ===");
        output.WriteLine(console.Output);

        Assert.Contains("env=prod", console.Output);
        Assert.Contains("region=us-east-1", console.Output);
        Assert.Contains("team=platform", console.Output);
    }

    [Fact]
    public void RenderKubeLsTable_LongClusterName_Truncated()
    {
        var clusters = new List<ClusterResponse>
        {
            new()
            {
                Id = "long-01",
                Name = "this-is-an-extremely-long-cluster-name-that-should-be-truncated",
                Status = "Connected",
                KubernetesVersion = "1.30.0",
                Labels = new(),
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        KubeLsCommand.RenderKubeLsTable(console, clusters, currentContext: null);

        output.WriteLine("=== Long Cluster Name (truncated to 24) ===");
        output.WriteLine(console.Output);

        // Full name should not appear — truncated at 24 chars.
        Assert.DoesNotContain("this-is-an-extremely-long-cluster-name-that-should-be-truncated", console.Output);
    }
}
