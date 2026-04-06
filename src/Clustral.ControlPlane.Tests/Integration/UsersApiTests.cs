using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class UsersApiTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private const string UsersUrl = "/api/v1/users";
    private const string ClustersUrl = "/api/v1/clusters";
    private const string RolesUrl = "/api/v1/roles";

    [Fact]
    public async Task GetMe_Returns200_AutoCreatesUser()
    {
        // The UserSyncFilter auto-creates the user on any authenticated request.
        var sub = $"user-me-{Guid.NewGuid().ToString("N")[..8]}";
        var client = factory.CreateAuthenticatedClient(sub, $"{sub}@test.com", "Me User");

        var response = await client.GetAsync($"{UsersUrl}/me");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {UsersUrl}/me => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal($"{sub}@test.com", doc.RootElement.GetProperty("email").GetString());
        Assert.Equal("Me User", doc.RootElement.GetProperty("displayName").GetString());
        Assert.True(doc.RootElement.TryGetProperty("assignments", out var assignments));
        Assert.Equal(JsonValueKind.Array, assignments.ValueKind);
    }

    [Fact]
    public async Task ListUsers_Returns200_IncludesTestUser()
    {
        var sub = $"user-list-{Guid.NewGuid().ToString("N")[..8]}";
        var email = $"{sub}@test.com";
        var client = factory.CreateAuthenticatedClient(sub, email, "List User");

        // Trigger user creation via any authenticated endpoint.
        await client.GetAsync($"{UsersUrl}/me");

        var response = await client.GetAsync(UsersUrl);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {UsersUrl} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var users = doc.RootElement.GetProperty("users");
        Assert.Equal(JsonValueKind.Array, users.ValueKind);

        // Find our test user in the list.
        var found = false;
        foreach (var u in users.EnumerateArray())
        {
            if (u.GetProperty("email").GetString() == email)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, $"Expected user {email} in the list");
    }

    [Fact]
    public async Task AssignRole_Returns201()
    {
        var sub = $"user-assign-{Guid.NewGuid().ToString("N")[..8]}";
        var client = factory.CreateAuthenticatedClient(sub, $"{sub}@test.com", "Assign User");

        // Create user via /me.
        var meResponse = await client.GetAsync($"{UsersUrl}/me");
        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        var userId = meDoc.RootElement.GetProperty("id").GetGuid();

        // Create cluster and role.
        var clusterName = $"cluster-{Guid.NewGuid().ToString("N")[..8]}";
        var clusterRes = await client.PostAsJsonAsync(ClustersUrl, new { name = clusterName });
        using var clusterDoc = JsonDocument.Parse(await clusterRes.Content.ReadAsStringAsync());
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetGuid();

        var roleName = $"role-{Guid.NewGuid().ToString("N")[..8]}";
        var roleRes = await client.PostAsJsonAsync(RolesUrl, new { name = roleName });
        using var roleDoc = JsonDocument.Parse(await roleRes.Content.ReadAsStringAsync());
        var roleId = roleDoc.RootElement.GetProperty("id").GetGuid();

        // Assign role to user on cluster.
        var response = await client.PostAsJsonAsync(
            $"{UsersUrl}/{userId}/assignments",
            new { roleId, clusterId });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {UsersUrl}/{userId}/assignments => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(userId, doc.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(roleId, doc.RootElement.GetProperty("roleId").GetGuid());
        Assert.Equal(clusterId, doc.RootElement.GetProperty("clusterId").GetGuid());
    }

    [Fact]
    public async Task AssignRoleUpsert_ReplacesExistingAssignment()
    {
        var sub = $"user-upsert-{Guid.NewGuid().ToString("N")[..8]}";
        var client = factory.CreateAuthenticatedClient(sub, $"{sub}@test.com", "Upsert User");

        var meResponse = await client.GetAsync($"{UsersUrl}/me");
        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        var userId = meDoc.RootElement.GetProperty("id").GetGuid();

        // Create cluster and two roles.
        var clusterName = $"cluster-{Guid.NewGuid().ToString("N")[..8]}";
        var clusterRes = await client.PostAsJsonAsync(ClustersUrl, new { name = clusterName });
        using var clusterDoc = JsonDocument.Parse(await clusterRes.Content.ReadAsStringAsync());
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetGuid();

        var role1Name = $"role1-{Guid.NewGuid().ToString("N")[..8]}";
        var role1Res = await client.PostAsJsonAsync(RolesUrl, new { name = role1Name });
        using var role1Doc = JsonDocument.Parse(await role1Res.Content.ReadAsStringAsync());
        var role1Id = role1Doc.RootElement.GetProperty("id").GetGuid();

        var role2Name = $"role2-{Guid.NewGuid().ToString("N")[..8]}";
        var role2Res = await client.PostAsJsonAsync(RolesUrl, new { name = role2Name });
        using var role2Doc = JsonDocument.Parse(await role2Res.Content.ReadAsStringAsync());
        var role2Id = role2Doc.RootElement.GetProperty("id").GetGuid();

        // First assignment with role1.
        await client.PostAsJsonAsync(
            $"{UsersUrl}/{userId}/assignments",
            new { roleId = role1Id, clusterId });

        // Upsert with role2 on the same cluster.
        var response = await client.PostAsJsonAsync(
            $"{UsersUrl}/{userId}/assignments",
            new { roleId = role2Id, clusterId });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {UsersUrl}/{userId}/assignments (upsert) => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(role2Id, doc.RootElement.GetProperty("roleId").GetGuid());

        // Verify only one assignment exists for this user+cluster.
        var assignmentsRes = await client.GetAsync($"{UsersUrl}/{userId}/assignments");
        using var assignDoc = JsonDocument.Parse(await assignmentsRes.Content.ReadAsStringAsync());
        var assignments = assignDoc.RootElement.GetProperty("assignments");
        var matchingCount = 0;
        foreach (var a in assignments.EnumerateArray())
        {
            if (a.GetProperty("clusterId").GetGuid() == clusterId)
                matchingCount++;
        }
        Assert.Equal(1, matchingCount);
    }

    [Fact]
    public async Task DeleteAssignment_Returns204()
    {
        var sub = $"user-del-{Guid.NewGuid().ToString("N")[..8]}";
        var client = factory.CreateAuthenticatedClient(sub, $"{sub}@test.com", "Delete User");

        var meResponse = await client.GetAsync($"{UsersUrl}/me");
        using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        var userId = meDoc.RootElement.GetProperty("id").GetGuid();

        // Create cluster and role.
        var clusterName = $"cluster-{Guid.NewGuid().ToString("N")[..8]}";
        var clusterRes = await client.PostAsJsonAsync(ClustersUrl, new { name = clusterName });
        using var clusterDoc = JsonDocument.Parse(await clusterRes.Content.ReadAsStringAsync());
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetGuid();

        var roleName = $"role-{Guid.NewGuid().ToString("N")[..8]}";
        var roleRes = await client.PostAsJsonAsync(RolesUrl, new { name = roleName });
        using var roleDoc = JsonDocument.Parse(await roleRes.Content.ReadAsStringAsync());
        var roleId = roleDoc.RootElement.GetProperty("id").GetGuid();

        // Create an assignment.
        var assignRes = await client.PostAsJsonAsync(
            $"{UsersUrl}/{userId}/assignments",
            new { roleId, clusterId });
        using var assignDoc = JsonDocument.Parse(await assignRes.Content.ReadAsStringAsync());
        var assignmentId = assignDoc.RootElement.GetProperty("id").GetGuid();

        // Delete the assignment.
        var response = await client.DeleteAsync($"{UsersUrl}/{userId}/assignments/{assignmentId}");
        output.WriteLine($"DELETE {UsersUrl}/{userId}/assignments/{assignmentId} => {(int)response.StatusCode}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
