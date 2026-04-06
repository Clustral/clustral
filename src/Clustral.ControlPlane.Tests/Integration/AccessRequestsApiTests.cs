using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AccessRequestsApiTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private const string AccessRequestsUrl = "/api/v1/access-requests";
    private const string ClustersUrl = "/api/v1/clusters";
    private const string RolesUrl = "/api/v1/roles";
    private const string UsersUrl = "/api/v1/users";

    /// <summary>
    /// Creates a cluster and role using the admin client, returns (clusterId, roleId).
    /// </summary>
    private async Task<(Guid ClusterId, Guid RoleId)> CreateClusterAndRoleAsync(HttpClient adminClient)
    {
        var clusterName = $"cluster-{Guid.NewGuid().ToString("N")[..8]}";
        var clusterRes = await adminClient.PostAsJsonAsync(ClustersUrl, new { name = clusterName });
        using var clusterDoc = JsonDocument.Parse(await clusterRes.Content.ReadAsStringAsync());
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetGuid();

        var roleName = $"role-{Guid.NewGuid().ToString("N")[..8]}";
        var roleRes = await adminClient.PostAsJsonAsync(RolesUrl, new { name = roleName });
        using var roleDoc = JsonDocument.Parse(await roleRes.Content.ReadAsStringAsync());
        var roleId = roleDoc.RootElement.GetProperty("id").GetGuid();

        return (clusterId, roleId);
    }

    /// <summary>
    /// Creates a user by calling GET /users/me with the given client.
    /// Returns the user's ID.
    /// </summary>
    private async Task<Guid> EnsureUserAsync(HttpClient client)
    {
        var meRes = await client.GetAsync($"{UsersUrl}/me");
        using var meDoc = JsonDocument.Parse(await meRes.Content.ReadAsStringAsync());
        return meDoc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task CreateAccessRequest_Returns201()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        // Use a different user (no static assignment).
        var requesterSub = $"requester-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "Requester");
        await EnsureUserAsync(requesterClient);

        var response = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new
        {
            roleId,
            clusterId,
            reason = "Need access for debugging"
        });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AccessRequestsUrl} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Pending", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(clusterId, doc.RootElement.GetProperty("clusterId").GetGuid());
        Assert.Equal(roleId, doc.RootElement.GetProperty("roleId").GetGuid());
    }

    [Fact]
    public async Task CreateAccessRequest_DuplicatePending_Returns409()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        var requesterSub = $"requester-dup-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "Dup Requester");
        await EnsureUserAsync(requesterClient);

        // First request succeeds.
        var first = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "first" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Second request for the same cluster should conflict.
        var second = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "second" });
        var body = await second.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AccessRequestsUrl} (duplicate) => {(int)second.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task ListAccessRequests_MineFilter_Returns200()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        var requesterSub = $"requester-list-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "List Requester");
        await EnsureUserAsync(requesterClient);

        await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "list test" });

        var response = await requesterClient.GetAsync($"{AccessRequestsUrl}?mine=true");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {AccessRequestsUrl}?mine=true => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var requests = doc.RootElement.GetProperty("requests");
        Assert.Equal(JsonValueKind.Array, requests.ValueKind);
        Assert.True(requests.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ApproveAccessRequest_Returns200_WithGrantExpiresAt()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        var requesterSub = $"requester-approve-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "Approve Requester");
        await EnsureUserAsync(requesterClient);

        var createRes = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "approve test" });
        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var requestId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Admin approves.
        var response = await adminClient.PostAsJsonAsync(
            $"{AccessRequestsUrl}/{requestId}/approve",
            new { });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AccessRequestsUrl}/{requestId}/approve => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Approved", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.TryGetProperty("grantExpiresAt", out var grantExpiry));
        Assert.NotEqual(JsonValueKind.Null, grantExpiry.ValueKind);
    }

    [Fact]
    public async Task DenyAccessRequest_Returns200_WithDenialReason()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        var requesterSub = $"requester-deny-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "Deny Requester");
        await EnsureUserAsync(requesterClient);

        var createRes = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "deny test" });
        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var requestId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Admin denies.
        var response = await adminClient.PostAsJsonAsync(
            $"{AccessRequestsUrl}/{requestId}/deny",
            new { reason = "Not justified" });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AccessRequestsUrl}/{requestId}/deny => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Denied", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Not justified", doc.RootElement.GetProperty("denialReason").GetString());
    }

    [Fact]
    public async Task ApproveNonPendingRequest_Returns409()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        var requesterSub = $"requester-nonpend-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "NonPending Requester");
        await EnsureUserAsync(requesterClient);

        var createRes = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "nonpend test" });
        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var requestId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Approve first.
        await adminClient.PostAsJsonAsync($"{AccessRequestsUrl}/{requestId}/approve", new { });

        // Try to approve again — should fail with 409.
        var response = await adminClient.PostAsJsonAsync(
            $"{AccessRequestsUrl}/{requestId}/approve",
            new { });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AccessRequestsUrl}/{requestId}/approve (non-pending) => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task RevokeGrant_Returns200()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        var requesterSub = $"requester-revoke-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "Revoke Requester");
        await EnsureUserAsync(requesterClient);

        var createRes = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "revoke test" });
        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var requestId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Approve first.
        await adminClient.PostAsJsonAsync($"{AccessRequestsUrl}/{requestId}/approve", new { });

        // Then revoke.
        var response = await adminClient.PostAsJsonAsync(
            $"{AccessRequestsUrl}/{requestId}/revoke",
            new { reason = "No longer needed" });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AccessRequestsUrl}/{requestId}/revoke => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Revoked", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.TryGetProperty("revokedAt", out var revokedAt));
        Assert.NotEqual(JsonValueKind.Null, revokedAt.ValueKind);
    }

    [Fact]
    public async Task ActiveGrantsFilter_Returns200()
    {
        var adminClient = factory.CreateAuthenticatedClient();
        var (clusterId, roleId) = await CreateClusterAndRoleAsync(adminClient);

        var requesterSub = $"requester-active-{Guid.NewGuid().ToString("N")[..8]}";
        var requesterClient = factory.CreateAuthenticatedClient(requesterSub, $"{requesterSub}@test.com", "Active Requester");
        await EnsureUserAsync(requesterClient);

        var createRes = await requesterClient.PostAsJsonAsync(AccessRequestsUrl, new { roleId, clusterId, reason = "active test" });
        using var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var requestId = createDoc.RootElement.GetProperty("id").GetGuid();

        // Approve to create an active grant.
        await adminClient.PostAsJsonAsync($"{AccessRequestsUrl}/{requestId}/approve", new { });

        // Query active grants.
        var response = await adminClient.GetAsync($"{AccessRequestsUrl}?active=true");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {AccessRequestsUrl}?active=true => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var requests = doc.RootElement.GetProperty("requests");
        Assert.Equal(JsonValueKind.Array, requests.ValueKind);

        // At least the one we just approved should be active.
        var found = false;
        foreach (var r in requests.EnumerateArray())
        {
            if (r.GetProperty("id").GetGuid() == requestId)
            {
                found = true;
                Assert.Equal("Approved", r.GetProperty("status").GetString());
                break;
            }
        }
        Assert.True(found, "Expected to find the approved access request in active grants");
    }
}
