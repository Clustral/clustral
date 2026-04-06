using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Roles;

[Collection(Integration.IntegrationTestCollection.Name)]
public sealed class RolesIntegrationTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Create_ValidRole_Returns201()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"role-{Guid.NewGuid():N}"[..20];

        var response = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name, description = "Test role", kubernetesGroups = new[] { "system:authenticated" },
        });

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Create: {(int)response.StatusCode} {body[..Math.Min(200, body.Length)]}");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("name").GetString().Should().Be(name);
    }

    [Fact]
    public async Task Create_EmptyName_Returns422()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = "", description = "No name",
        });

        output.WriteLine($"Empty name: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"role-dup-{Guid.NewGuid():N}"[..20];

        await client.PostAsJsonAsync("/api/v1/roles", new { name, description = "" });
        var response = await client.PostAsJsonAsync("/api/v1/roles", new { name, description = "" });

        output.WriteLine($"Duplicate: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_ReturnsOk()
    {
        var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/v1/roles");

        output.WriteLine($"List: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_ExistingRole_Returns204()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"role-del-{Guid.NewGuid():N}"[..20];

        var createResp = await client.PostAsJsonAsync("/api/v1/roles", new { name, description = "" });
        var createBody = await createResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createBody);
        var id = doc.RootElement.GetProperty("id").GetString();

        var deleteResp = await client.DeleteAsync($"/api/v1/roles/{id}");

        output.WriteLine($"Delete: {(int)deleteResp.StatusCode}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
