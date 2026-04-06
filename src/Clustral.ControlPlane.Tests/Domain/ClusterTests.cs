using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public class ClusterTests(ITestOutputHelper output)
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var cluster = new Cluster();

        output.WriteLine($"Status:            {cluster.Status}");
        output.WriteLine($"Name:              \"{cluster.Name}\"");
        output.WriteLine($"Description:       \"{cluster.Description}\"");
        output.WriteLine($"AgentPublicKeyPem: \"{cluster.AgentPublicKeyPem}\"");
        output.WriteLine($"BootstrapToken:    {cluster.BootstrapTokenHash ?? "null"}");
        output.WriteLine($"KubernetesVersion: {cluster.KubernetesVersion ?? "null"}");
        output.WriteLine($"Labels:            {cluster.Labels.Count} items");
        output.WriteLine($"LastSeenAt:        {cluster.LastSeenAt?.ToString() ?? "null"}");

        Assert.Equal(ClusterStatus.Pending, cluster.Status);
        Assert.Equal(string.Empty, cluster.Name);
        Assert.Empty(cluster.Labels);
        Assert.Null(cluster.BootstrapTokenHash);
        Assert.Null(cluster.KubernetesVersion);
        Assert.Null(cluster.LastSeenAt);
    }

    [Fact]
    public void ClusterStatus_HasExpectedValues()
    {
        output.WriteLine($"Pending:      {(int)ClusterStatus.Pending}");
        output.WriteLine($"Connected:    {(int)ClusterStatus.Connected}");
        output.WriteLine($"Disconnected: {(int)ClusterStatus.Disconnected}");

        Assert.Equal(0, (int)ClusterStatus.Pending);
        Assert.Equal(1, (int)ClusterStatus.Connected);
        Assert.Equal(2, (int)ClusterStatus.Disconnected);
    }

    [Fact]
    public void Labels_CanStoreMultipleEntries()
    {
        var cluster = new Cluster
        {
            Labels = new Dictionary<string, string>
            {
                ["env"] = "production",
                ["region"] = "eu-west-1",
                ["team"] = "platform",
            },
        };

        output.WriteLine($"Labels: {string.Join(", ", cluster.Labels.Select(kv => $"{kv.Key}={kv.Value}"))}");

        Assert.Equal(3, cluster.Labels.Count);
        Assert.Equal("production", cluster.Labels["env"]);
    }

    [Fact]
    public void BootstrapTokenHash_ClearedAfterConsumption()
    {
        var cluster = new Cluster
        {
            BootstrapTokenHash = "abc123hash",
        };

        output.WriteLine($"Before: BootstrapTokenHash = \"{cluster.BootstrapTokenHash}\"");

        // Simulate consumption (as AuthServiceImpl does).
        cluster.BootstrapTokenHash = null;

        output.WriteLine($"After:  BootstrapTokenHash = {cluster.BootstrapTokenHash ?? "null"}");

        Assert.Null(cluster.BootstrapTokenHash);
    }
}
