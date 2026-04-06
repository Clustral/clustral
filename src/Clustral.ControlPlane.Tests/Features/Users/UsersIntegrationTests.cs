using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Users;

[Collection(Integration.IntegrationTestCollection.Name)]
public sealed class UsersIntegrationTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    [Fact]
    public async Task GetMe_Returns200_WithProfile()
    {
        var client = factory.CreateAuthenticatedClient("user-me-test", "me@test.com", "Me User");
        var response = await client.GetAsync("/api/v1/users/me");

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET /users/me: {(int)response.StatusCode} {body[..Math.Min(200, body.Length)]}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("email").GetString().Should().Be("me@test.com");
    }

    [Fact]
    public async Task ListUsers_Returns200()
    {
        var client = factory.CreateAuthenticatedClient();
        // Ensure at least one user exists.
        await client.GetAsync("/api/v1/users/me");

        var response = await client.GetAsync("/api/v1/users");

        output.WriteLine($"GET /users: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AssignRole_Returns201()
    {
        var client = factory.CreateAuthenticatedClient("user-assign-test", "assign@test.com");

        // Ensure user exists.
        var meResp = await client.GetAsync("/api/v1/users/me");
        var meBody = await meResp.Content.ReadAsStringAsync();
        using var meDoc = JsonDocument.Parse(meBody);
        var userId = meDoc.RootElement.GetProperty("id").GetString()!;

        // Create role + cluster.
        var roleName = $"role-assign-{Guid.NewGuid():N}"[..20];
        var roleResp = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = roleName, description = "", kubernetesGroups = new[] { "system:authenticated" },
        });
        roleResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var roleBody = await roleResp.Content.ReadAsStringAsync();
        using var roleDoc = JsonDocument.Parse(roleBody);
        var roleId = roleDoc.RootElement.GetProperty("id").GetString()!;

        var clusterName = $"cluster-assign-{Guid.NewGuid():N}"[..20];
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name = clusterName, description = "", agentPublicKeyPem = "",
        });
        clusterResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var clusterBody = await clusterResp.Content.ReadAsStringAsync();
        using var clusterDoc = JsonDocument.Parse(clusterBody);
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetString()!;

        // Assign.
        var assignResp = await client.PostAsJsonAsync($"/api/v1/users/{userId}/assignments", new
        {
            roleId, clusterId,
        });

        var assignBody = await assignResp.Content.ReadAsStringAsync();
        output.WriteLine($"Assign: {(int)assignResp.StatusCode} {assignBody[..Math.Min(200, assignBody.Length)]}");

        assignResp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var assignDoc = JsonDocument.Parse(assignBody);
        assignDoc.RootElement.GetProperty("roleName").GetString().Should().Be(roleName);
    }

    [Fact]
    public async Task RemoveAssignment_Returns204()
    {
        var client = factory.CreateAuthenticatedClient("user-remove-test", "remove@test.com");

        // Setup: user + role + cluster + assignment.
        var meResp = await client.GetAsync("/api/v1/users/me");
        var meBody = await meResp.Content.ReadAsStringAsync();
        using var meDoc = JsonDocument.Parse(meBody);
        var userId = meDoc.RootElement.GetProperty("id").GetString()!;

        var roleName = $"role-rm-{Guid.NewGuid():N}"[..20];
        var roleResp = await client.PostAsJsonAsync("/api/v1/roles", new { name = roleName, description = "" });
        var roleBody = await roleResp.Content.ReadAsStringAsync();
        using var roleDoc = JsonDocument.Parse(roleBody);
        var roleId = roleDoc.RootElement.GetProperty("id").GetString()!;

        var clusterName = $"cl-rm-{Guid.NewGuid():N}"[..20];
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name = clusterName, description = "", agentPublicKeyPem = "",
        });
        var clusterBody = await clusterResp.Content.ReadAsStringAsync();
        using var clusterDoc = JsonDocument.Parse(clusterBody);
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetString()!;

        var assignResp = await client.PostAsJsonAsync($"/api/v1/users/{userId}/assignments", new { roleId, clusterId });
        var assignBody = await assignResp.Content.ReadAsStringAsync();
        using var assignDoc = JsonDocument.Parse(assignBody);
        var assignmentId = assignDoc.RootElement.GetProperty("id").GetString()!;

        // Remove.
        var deleteResp = await client.DeleteAsync($"/api/v1/users/{userId}/assignments/{assignmentId}");

        output.WriteLine($"Remove assignment: {(int)deleteResp.StatusCode}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveAssignment_Nonexistent_Returns404()
    {
        var client = factory.CreateAuthenticatedClient();
        var fakeUserId = Guid.NewGuid();
        var fakeAssignmentId = Guid.NewGuid();

        var response = await client.DeleteAsync($"/api/v1/users/{fakeUserId}/assignments/{fakeAssignmentId}");

        output.WriteLine($"Remove nonexistent: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
