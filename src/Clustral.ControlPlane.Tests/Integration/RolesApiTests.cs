using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class RolesApiTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private const string BaseUrl = "/api/v1/roles";

    [Fact]
    public async Task CreateRole_Returns201_WithNameDescriptionGroups()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsJsonAsync(BaseUrl, new
        {
            name,
            description = "integration test role",
            kubernetesGroups = new[] { "dev", "staging" }
        });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {BaseUrl} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(name, doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("integration test role", doc.RootElement.GetProperty("description").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("kubernetesGroups").GetArrayLength());
    }

    [Fact]
    public async Task CreateDuplicateRoleName_Returns409_WithProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        var first = await client.PostAsJsonAsync(BaseUrl, new { name });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(BaseUrl, new { name });
        var body = await second.Content.ReadAsStringAsync();
        output.WriteLine($"POST {BaseUrl} (duplicate) => {(int)second.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task ListRoles_Returns200_WithArray()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsJsonAsync(BaseUrl, new { name });

        var response = await client.GetAsync(BaseUrl);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {BaseUrl} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("roles", out var roles));
        Assert.Equal(JsonValueKind.Array, roles.ValueKind);
        Assert.True(roles.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task UpdateRole_Returns200_WithUpdatedFields()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        var createResponse = await client.PostAsJsonAsync(BaseUrl, new { name });
        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var roleId = createDoc.RootElement.GetProperty("id").GetGuid();

        var updatedName = $"updated-{Guid.NewGuid().ToString("N")[..8]}";
        var response = await client.PutAsJsonAsync($"{BaseUrl}/{roleId}", new
        {
            name = updatedName,
            description = "updated description"
        });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"PUT {BaseUrl}/{roleId} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(updatedName, doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("updated description", doc.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public async Task UpdateNonexistentRole_Returns404_WithProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var fakeId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync($"{BaseUrl}/{fakeId}", new
        {
            name = "does-not-matter"
        });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"PUT {BaseUrl}/{fakeId} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task DeleteRole_Returns204()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"test-{Guid.NewGuid().ToString("N")[..8]}";

        var createResponse = await client.PostAsJsonAsync(BaseUrl, new { name });
        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var roleId = createDoc.RootElement.GetProperty("id").GetGuid();

        var response = await client.DeleteAsync($"{BaseUrl}/{roleId}");
        output.WriteLine($"DELETE {BaseUrl}/{roleId} => {(int)response.StatusCode}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
