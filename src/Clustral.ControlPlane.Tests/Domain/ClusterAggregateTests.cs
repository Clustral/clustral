using Clustral.ControlPlane.Domain;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public sealed class ClusterAggregateTests(ITestOutputHelper output)
{
    [Fact]
    public void Create_SetsPendingStatus()
    {
        var cluster = Cluster.Create("prod", "Production", "pem-key", "hash-abc");

        output.WriteLine($"Name: {cluster.Name}, Status: {cluster.Status}");
        cluster.Id.Should().NotBe(Guid.Empty);
        cluster.Name.Should().Be("prod");
        cluster.Status.Should().Be(ClusterStatus.Pending);
        cluster.BootstrapTokenHash.Should().Be("hash-abc");
        cluster.Labels.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithLabels()
    {
        var labels = new Dictionary<string, string> { ["env"] = "prod", ["region"] = "eu" };
        var cluster = Cluster.Create("prod", "Production", "pem", "hash", labels);

        cluster.Labels.Should().HaveCount(2);
        cluster.Labels["env"].Should().Be("prod");
    }

    [Fact]
    public void Connect_SetsConnectedAndLastSeen()
    {
        var cluster = Cluster.Create("test", "Test", "pem", "hash");

        cluster.Connect("v1.29.0");

        output.WriteLine($"Status: {cluster.Status}, K8s: {cluster.KubernetesVersion}");
        cluster.Status.Should().Be(ClusterStatus.Connected);
        cluster.LastSeenAt.Should().NotBeNull();
        cluster.KubernetesVersion.Should().Be("v1.29.0");
    }

    [Fact]
    public void Disconnect_SetsDisconnected()
    {
        var cluster = Cluster.Create("test", "Test", "pem", "hash");
        cluster.Connect();

        cluster.Disconnect();

        cluster.Status.Should().Be(ClusterStatus.Disconnected);
    }

    [Fact]
    public void RecordHeartbeat_UpdatesLastSeen()
    {
        var cluster = Cluster.Create("test", "Test", "pem", "hash");
        cluster.Connect();
        var firstSeen = cluster.LastSeenAt;

        cluster.RecordHeartbeat("v1.30.0");

        cluster.LastSeenAt.Should().BeOnOrAfter(firstSeen!.Value);
        cluster.KubernetesVersion.Should().Be("v1.30.0");
    }

    [Fact]
    public void ConsumeBootstrapToken_ClearsHash()
    {
        var cluster = Cluster.Create("test", "Test", "pem", "token-hash");

        cluster.BootstrapTokenHash.Should().Be("token-hash");

        cluster.ConsumeBootstrapToken();

        output.WriteLine($"BootstrapTokenHash: {cluster.BootstrapTokenHash ?? "null"}");
        cluster.BootstrapTokenHash.Should().BeNull();
    }
}
