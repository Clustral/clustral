using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

public class ClusterModelsTests(ITestOutputHelper output)
{
    [Fact]
    public void ClusterResponse_From_MapsAllFields()
    {
        var cluster = new Cluster
        {
            Id = Guid.NewGuid(),
            Name = "production",
            Description = "Main production cluster",
            Status = ClusterStatus.Connected,
            KubernetesVersion = "1.30.1",
            RegisteredAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            LastSeenAt = DateTimeOffset.UtcNow,
            Labels = new() { ["env"] = "prod", ["region"] = "eu-west-1" },
        };

        var response = ClusterResponse.From(cluster);

        output.WriteLine($"=== ClusterResponse.From ===");
        output.WriteLine($"  ID:      {response.Id}");
        output.WriteLine($"  Name:    {response.Name}");
        output.WriteLine($"  Status:  {response.Status}");
        output.WriteLine($"  K8s:     {response.KubernetesVersion}");
        output.WriteLine($"  Labels:  {string.Join(", ", response.Labels.Select(kv => $"{kv.Key}={kv.Value}"))}");

        Assert.Equal(cluster.Id, response.Id);
        Assert.Equal("production", response.Name);
        Assert.Equal("Connected", response.Status);
        Assert.Equal("1.30.1", response.KubernetesVersion);
        Assert.Equal(2, response.Labels.Count);
    }

    [Fact]
    public void ClusterResponse_From_StatusToString()
    {
        foreach (ClusterStatus status in Enum.GetValues<ClusterStatus>())
        {
            var cluster = new Cluster { Status = status };
            var response = ClusterResponse.From(cluster);

            output.WriteLine($"ClusterStatus.{status} => \"{response.Status}\"");

            Assert.Equal(status.ToString(), response.Status);
        }
    }

    [Fact]
    public void ClusterResponse_From_NullOptionals()
    {
        var cluster = new Cluster
        {
            KubernetesVersion = null,
            LastSeenAt = null,
        };

        var response = ClusterResponse.From(cluster);

        output.WriteLine($"KubernetesVersion: {response.KubernetesVersion ?? "null"}");
        output.WriteLine($"LastSeenAt:        {response.LastSeenAt?.ToString() ?? "null"}");

        Assert.Null(response.KubernetesVersion);
        Assert.Null(response.LastSeenAt);
    }

    [Fact]
    public void RegisterClusterRestRequest_RequiredFields()
    {
        var request = new RegisterClusterRestRequest(
            Name: "test-cluster",
            Description: "A test cluster",
            AgentPublicKeyPem: "PEM-KEY-DATA",
            Labels: new() { ["env"] = "dev" });

        output.WriteLine($"Name:    {request.Name}");
        output.WriteLine($"PubKey:  {request.AgentPublicKeyPem[..10]}...");
        output.WriteLine($"Labels:  {request.Labels?.Count ?? 0}");

        Assert.Equal("test-cluster", request.Name);
    }

    [Fact]
    public void RegisterClusterRestRequest_OptionalDefaults()
    {
        var request = new RegisterClusterRestRequest(Name: "minimal");

        output.WriteLine($"Description: \"{request.Description}\"");
        output.WriteLine($"PubKey:      \"{request.AgentPublicKeyPem}\"");
        output.WriteLine($"Labels:      {request.Labels?.ToString() ?? "null"}");

        Assert.Equal("", request.Description);
        Assert.Equal("", request.AgentPublicKeyPem);
        Assert.Null(request.Labels);
    }

    [Fact]
    public void ListClustersQuery_Defaults()
    {
        var query = new Clustral.ControlPlane.Features.Clusters.Queries.ListClustersQuery(null, 50, null);

        output.WriteLine($"StatusFilter:  {query.StatusFilter ?? "null"}");
        output.WriteLine($"PageSize:      {query.PageSize}");
        output.WriteLine($"PageToken:     {query.PageToken ?? "null"}");

        Assert.Null(query.StatusFilter);
        Assert.Equal(50, query.PageSize);
        Assert.Null(query.PageToken);
    }
}
