using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Clusters;

/// <summary>
/// Integration tests for the Clusters feature slice using FluentAssertions.
/// Validates the full MediatR pipeline: controller → validator → handler → MongoDB.
/// </summary>
[Collection(Integration.IntegrationTestCollection.Name)]
public sealed class ClustersIntegrationTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Register_ValidCluster_Returns201()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"slice-{Guid.NewGuid():N}"[..20];

        var response = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "Feature slice test", agentPublicKeyPem = "",
        });

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Register: {(int)response.StatusCode} {body[..Math.Min(200, body.Length)]}");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("clusterId").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("bootstrapToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_EmptyName_Returns422_ViaValidator()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name = "", description = "No name", agentPublicKeyPem = "",
        });

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Empty name: {(int)response.StatusCode} {body[..Math.Min(300, body.Length)]}");

        // FluentValidation via ValidationBehavior returns 422 (Validation error).
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Get_ExistingCluster_Returns200()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"slice-get-{Guid.NewGuid():N}"[..20];

        // Create first.
        var createResp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "", agentPublicKeyPem = "",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResp.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("clusterId").GetString();

        // Get.
        var getResp = await client.GetAsync($"/api/v1/clusters/{id}");
        var getBody = await getResp.Content.ReadAsStringAsync();
        output.WriteLine($"Get: {(int)getResp.StatusCode} {getBody[..Math.Min(200, getBody.Length)]}");

        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var getDoc = JsonDocument.Parse(getBody);
        getDoc.RootElement.GetProperty("name").GetString().Should().Be(name);
    }

    [Fact]
    public async Task Delete_ExistingCluster_Returns204()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"slice-del-{Guid.NewGuid():N}"[..20];

        var createResp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "", agentPublicKeyPem = "",
        });
        var createBody = await createResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createBody);
        var id = doc.RootElement.GetProperty("clusterId").GetString();

        var deleteResp = await client.DeleteAsync($"/api/v1/clusters/{id}");
        output.WriteLine($"Delete: {(int)deleteResp.StatusCode}");

        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task List_ReturnsOk_WithArray()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/v1/clusters");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"List: {(int)response.StatusCode}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("clusters").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
