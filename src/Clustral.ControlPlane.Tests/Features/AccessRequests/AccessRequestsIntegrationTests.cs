using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.AccessRequests;

[Collection(Integration.IntegrationTestCollection.Name)]
public sealed class AccessRequestsIntegrationTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private async Task<(string ClusterId, string RoleId)> SetupClusterAndRole(HttpClient client)
    {
        var clusterName = $"ar-{Guid.NewGuid():N}"[..20];
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name = clusterName, description = "", agentPublicKeyPem = "",
        });
        var clusterBody = await clusterResp.Content.ReadAsStringAsync();
        using var clusterDoc = JsonDocument.Parse(clusterBody);
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetString()!;

        var roleName = $"ar-{Guid.NewGuid():N}"[..20];
        var roleResp = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = roleName, description = "", kubernetesGroups = new[] { "system:authenticated" },
        });
        var roleBody = await roleResp.Content.ReadAsStringAsync();
        using var roleDoc = JsonDocument.Parse(roleBody);
        var roleId = roleDoc.RootElement.GetProperty("id").GetString()!;

        return (clusterId, roleId);
    }

    [Fact]
    public async Task CreateRequest_Returns201()
    {
        var userClient = factory.CreateAuthenticatedClient("ar-user-1", "ar-user1@test.com");
        await userClient.GetAsync("/api/v1/users/me"); // ensure user exists

        var adminClient = factory.CreateAuthenticatedClient("ar-admin-1", "ar-admin1@test.com");
        var (clusterId, roleId) = await SetupClusterAndRole(adminClient);

        var response = await userClient.PostAsJsonAsync("/api/v1/access-requests", new
        {
            roleId, clusterId, reason = "deploy hotfix",
        });

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Create: {(int)response.StatusCode} {body[..Math.Min(200, body.Length)]}");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task FullLifecycle_CreateApproveThenRevoke()
    {
        var userClient = factory.CreateAuthenticatedClient("ar-lifecycle", "ar-lifecycle@test.com");
        await userClient.GetAsync("/api/v1/users/me");

        var adminClient = factory.CreateAuthenticatedClient("ar-admin-lc", "ar-admin-lc@test.com");
        var (clusterId, roleId) = await SetupClusterAndRole(adminClient);

        // Create
        var createResp = await userClient.PostAsJsonAsync("/api/v1/access-requests", new
        {
            roleId, clusterId, reason = "lifecycle test",
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResp.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var requestId = createDoc.RootElement.GetProperty("id").GetString()!;

        output.WriteLine($"Created: {requestId}");

        // Approve
        var approveResp = await adminClient.PostAsJsonAsync(
            $"/api/v1/access-requests/{requestId}/approve", new { });
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var approveBody = await approveResp.Content.ReadAsStringAsync();
        using var approveDoc = JsonDocument.Parse(approveBody);
        approveDoc.RootElement.GetProperty("status").GetString().Should().Be("Approved");
        approveDoc.RootElement.GetProperty("grantExpiresAt").GetString().Should().NotBeNullOrEmpty();

        output.WriteLine($"Approved, grant expires: {approveDoc.RootElement.GetProperty("grantExpiresAt")}");

        // Revoke
        var revokeResp = await adminClient.PostAsJsonAsync(
            $"/api/v1/access-requests/{requestId}/revoke", new { reason = "no longer needed" });
        revokeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokeBody = await revokeResp.Content.ReadAsStringAsync();
        using var revokeDoc = JsonDocument.Parse(revokeBody);
        revokeDoc.RootElement.GetProperty("status").GetString().Should().Be("Revoked");

        output.WriteLine("Revoked successfully");
    }

    [Fact]
    public async Task DenyRequest_Returns200()
    {
        var userClient = factory.CreateAuthenticatedClient("ar-deny-user", "ar-deny@test.com");
        await userClient.GetAsync("/api/v1/users/me");

        var adminClient = factory.CreateAuthenticatedClient("ar-deny-admin", "ar-deny-admin@test.com");
        var (clusterId, roleId) = await SetupClusterAndRole(adminClient);

        var createResp = await userClient.PostAsJsonAsync("/api/v1/access-requests", new
        {
            roleId, clusterId, reason = "deny test",
        });
        var createBody = await createResp.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var requestId = createDoc.RootElement.GetProperty("id").GetString()!;

        var denyResp = await adminClient.PostAsJsonAsync(
            $"/api/v1/access-requests/{requestId}/deny", new { reason = "not authorized" });

        var denyBody = await denyResp.Content.ReadAsStringAsync();
        output.WriteLine($"Deny: {(int)denyResp.StatusCode} {denyBody[..Math.Min(200, denyBody.Length)]}");

        denyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var denyDoc = JsonDocument.Parse(denyBody);
        denyDoc.RootElement.GetProperty("status").GetString().Should().Be("Denied");
        denyDoc.RootElement.GetProperty("denialReason").GetString().Should().Be("not authorized");
    }

    [Fact]
    public async Task ListRequests_Mine_Returns200()
    {
        var client = factory.CreateAuthenticatedClient("ar-list-user", "ar-list@test.com");
        await client.GetAsync("/api/v1/users/me");

        var response = await client.GetAsync("/api/v1/access-requests?mine=true");

        output.WriteLine($"List mine: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRequest_Nonexistent_Returns404()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/v1/access-requests/{Guid.NewGuid()}");

        output.WriteLine($"Get nonexistent: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApproveNonPending_Returns409()
    {
        var userClient = factory.CreateAuthenticatedClient("ar-np-user", "ar-np@test.com");
        await userClient.GetAsync("/api/v1/users/me");

        var adminClient = factory.CreateAuthenticatedClient("ar-np-admin", "ar-np-admin@test.com");
        var (clusterId, roleId) = await SetupClusterAndRole(adminClient);

        // Create + approve
        var createResp = await userClient.PostAsJsonAsync("/api/v1/access-requests", new
        {
            roleId, clusterId, reason = "approve twice test",
        });
        var createBody = await createResp.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        var requestId = createDoc.RootElement.GetProperty("id").GetString()!;

        await adminClient.PostAsJsonAsync($"/api/v1/access-requests/{requestId}/approve", new { });

        // Try to approve again
        var secondApprove = await adminClient.PostAsJsonAsync(
            $"/api/v1/access-requests/{requestId}/approve", new { });

        output.WriteLine($"Second approve: {(int)secondApprove.StatusCode}");
        secondApprove.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
