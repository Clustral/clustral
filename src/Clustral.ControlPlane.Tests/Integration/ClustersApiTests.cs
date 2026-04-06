using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ClustersApiTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private const string BaseUrl = "/api/v1/clusters";

    [Fact]
    public async Task RegisterCluster_Returns201_WithClusterIdAndBootstrapToken()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsJsonAsync(BaseUrl, new { name, description = "integration test" });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {BaseUrl} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("clusterId", out var idProp));
        Assert.NotEqual(Guid.Empty, idProp.GetGuid());
        Assert.True(doc.RootElement.TryGetProperty("bootstrapToken", out var tokenProp));
        Assert.False(string.IsNullOrEmpty(tokenProp.GetString()));
    }

    [Fact]
    public async Task RegisterDuplicateName_Returns409_WithProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        // First registration should succeed.
        var first = await client.PostAsJsonAsync(BaseUrl, new { name });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Second registration with the same name should conflict.
        var second = await client.PostAsJsonAsync(BaseUrl, new { name });
        var body = await second.Content.ReadAsStringAsync();
        output.WriteLine($"POST {BaseUrl} (duplicate) => {(int)second.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task ListClusters_Returns200_WithArray()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        // Ensure at least one cluster exists.
        await client.PostAsJsonAsync(BaseUrl, new { name });

        var response = await client.GetAsync(BaseUrl);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {BaseUrl} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("clusters", out var clusters));
        Assert.Equal(JsonValueKind.Array, clusters.ValueKind);
        Assert.True(clusters.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task GetClusterById_Returns200()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        var createResponse = await client.PostAsJsonAsync(BaseUrl, new { name });
        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var clusterId = createDoc.RootElement.GetProperty("clusterId").GetGuid();

        var response = await client.GetAsync($"{BaseUrl}/{clusterId}");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {BaseUrl}/{clusterId} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(clusterId, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(name, doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetNonexistentCluster_Returns404_WithProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var fakeId = Guid.NewGuid();

        var response = await client.GetAsync($"{BaseUrl}/{fakeId}");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {BaseUrl}/{fakeId} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task DeleteCluster_Returns204()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        var createResponse = await client.PostAsJsonAsync(BaseUrl, new { name });
        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var clusterId = createDoc.RootElement.GetProperty("clusterId").GetGuid();

        var response = await client.DeleteAsync($"{BaseUrl}/{clusterId}");
        output.WriteLine($"DELETE {BaseUrl}/{clusterId} => {(int)response.StatusCode}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's gone.
        var getResponse = await client.GetAsync($"{BaseUrl}/{clusterId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteNonexistentCluster_Returns404_WithProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var fakeId = Guid.NewGuid();

        var response = await client.DeleteAsync($"{BaseUrl}/{fakeId}");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"DELETE {BaseUrl}/{fakeId} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }
}
