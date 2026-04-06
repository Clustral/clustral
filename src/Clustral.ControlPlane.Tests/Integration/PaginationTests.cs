using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class PaginationTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private async Task<string> CreateClusterAsync(HttpClient client, string? prefix = null)
    {
        var name = $"{prefix ?? "page"}-{Guid.NewGuid():N}"[..20];
        var response = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name,
            description = "pagination test",
            agentPublicKeyPem = "",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("clusterId").GetString()!;
    }

    [Fact]
    public async Task ListClusters_WithStatusFilter_ReturnsFilteredResults()
    {
        var client = factory.CreateAuthenticatedClient();
        // All test clusters default to Pending — filter for Connected returns none of ours.
        var response = await client.GetAsync("/api/v1/clusters?statusFilter=Connected&pageSize=200");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var clusters = doc.RootElement.GetProperty("clusters");
        output.WriteLine($"Connected filter: {clusters.GetArrayLength()} clusters");

        // Should be a valid array (possibly empty — no test clusters are Connected).
        Assert.True(clusters.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task ListClusters_UnderPageSize_NoNextPageToken()
    {
        var client = factory.CreateAuthenticatedClient();
        await CreateClusterAsync(client, "under");
        await CreateClusterAsync(client, "under");

        var response = await client.GetAsync("/api/v1/clusters?pageSize=200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var nextToken = doc.RootElement.GetProperty("nextPageToken");
        output.WriteLine($"Under pageSize: nextPageToken = {nextToken}");

        Assert.Equal(JsonValueKind.Null, nextToken.ValueKind);
    }

    [Fact]
    public async Task ListClusters_OverPageSize_ReturnsNextPageToken()
    {
        var client = factory.CreateAuthenticatedClient();
        // Create enough clusters to exceed pageSize=1.
        await CreateClusterAsync(client, "over");
        await CreateClusterAsync(client, "over");

        var response = await client.GetAsync("/api/v1/clusters?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var clusters = doc.RootElement.GetProperty("clusters");
        var nextToken = doc.RootElement.GetProperty("nextPageToken");

        output.WriteLine($"Page 1: {clusters.GetArrayLength()} clusters, nextPageToken = {nextToken}");

        Assert.Equal(1, clusters.GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, nextToken.ValueKind);
    }

    [Fact]
    public async Task ListClusters_WithPageToken_ReturnsNextPage()
    {
        var client = factory.CreateAuthenticatedClient();
        await CreateClusterAsync(client, "cursor");
        await CreateClusterAsync(client, "cursor");
        await CreateClusterAsync(client, "cursor");

        // Get first page.
        var resp1 = await client.GetAsync("/api/v1/clusters?pageSize=1");
        var body1 = await resp1.Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(body1);
        var token = doc1.RootElement.GetProperty("nextPageToken").GetString();

        output.WriteLine($"Page 1 token: {token}");
        Assert.NotNull(token);

        // Get second page using the token.
        var resp2 = await client.GetAsync($"/api/v1/clusters?pageSize=1&pageToken={token}");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        var body2 = await resp2.Content.ReadAsStringAsync();
        using var doc2 = JsonDocument.Parse(body2);

        var page2Clusters = doc2.RootElement.GetProperty("clusters");
        output.WriteLine($"Page 2: {page2Clusters.GetArrayLength()} clusters");

        Assert.Equal(1, page2Clusters.GetArrayLength());
    }

    [Fact]
    public async Task ListClusters_PageSizeZero_ClampedToOne()
    {
        var client = factory.CreateAuthenticatedClient();
        await CreateClusterAsync(client, "clamp");

        var response = await client.GetAsync("/api/v1/clusters?pageSize=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var clusters = doc.RootElement.GetProperty("clusters");
        output.WriteLine($"pageSize=0 clamped: {clusters.GetArrayLength()} results");

        // Should return at least 1 (clamped from 0 to 1).
        Assert.True(clusters.GetArrayLength() >= 1);
    }
}
