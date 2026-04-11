using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clustral.Cli.Config;
using Clustral.ControlPlane.Tests;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Integration;

/// <summary>
/// Integration tests that verify the CLI's wire types deserialize correctly
/// against the real ControlPlane API. Uses <see cref="ClustralWebApplicationFactory"/>
/// (Testcontainers MongoDB + TestAuthHandler) to spin up a real ControlPlane.
/// </summary>
[Collection(CliIntegrationTestCollection.Name)]
public sealed class CliIntegrationTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    // ── Version endpoint ──────────────────────────────────────────────────────

    [Fact]
    public async Task Version_DeserializesWithCliWireType()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/version");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var config = JsonSerializer.Deserialize(json, CliJsonContext.Default.ControlPlaneConfig);

        output.WriteLine($"Version: {config!.Version}");

        config.Should().NotBeNull();
        config!.Version.Should().NotBeNullOrEmpty("response should include version");
    }

    // ── Clusters ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListClusters_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient();

        // Seed
        var name = $"cli-cluster-{Guid.NewGuid().ToString()[..8]}";
        await client.PostAsJsonAsync("/api/v1/clusters", new { name });

        // List — same endpoint the CLI calls
        var response = await client.GetAsync("/api/v1/clusters");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.ClusterListResponse);

        output.WriteLine($"Clusters: {result!.Clusters.Count}");
        result.Should().NotBeNull();
        result!.Clusters.Should().Contain(c => c.Name == name);

        var cluster = result.Clusters.First(c => c.Name == name);
        cluster.Id.Should().NotBeNullOrEmpty();
        cluster.Status.Should().NotBeNullOrEmpty();
        cluster.RegisteredAt.Should().BeAfter(DateTimeOffset.MinValue);
        output.WriteLine($"  {cluster.Name}: {cluster.Status} (id={cluster.Id[..8]})");
    }

    // ── Users ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient();

        // Trigger user sync by calling any authenticated endpoint
        await client.GetAsync("/api/v1/users/me");

        var response = await client.GetAsync("/api/v1/users");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserListResponse);

        output.WriteLine($"Users: {result!.Users.Count}");
        result.Should().NotBeNull();
        result!.Users.Should().NotBeEmpty();

        var user = result.Users.First();
        user.Id.Should().NotBeNullOrEmpty();
        user.Email.Should().NotBeNullOrEmpty();
        output.WriteLine($"  {user.Email} (id={user.Id[..8]})");
    }

    // ── User profile ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfile_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/v1/users/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var profile = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserProfileResponse);

        output.WriteLine($"Profile: {profile!.Email} ({profile.DisplayName})");
        profile.Should().NotBeNull();
        profile!.Id.Should().NotBeNullOrEmpty();
        profile.Email.Should().Be(TestAuthHandler.DefaultEmail);
        profile.Assignments.Should().NotBeNull();
        profile.ActiveGrants.Should().NotBeNull();
    }

    // ── Roles ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListRoles_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient();

        // Seed
        var roleName = $"cli-role-{Guid.NewGuid().ToString()[..8]}";
        await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = roleName,
            description = "Test role",
            kubernetesGroups = new[] { "system:masters" },
        });

        var response = await client.GetAsync("/api/v1/roles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.RoleListResponse);

        output.WriteLine($"Roles: {result!.Roles.Count}");
        result.Should().NotBeNull();
        result!.Roles.Should().Contain(r => r.Name == roleName);

        var role = result.Roles.First(r => r.Name == roleName);
        role.Id.Should().NotBeNullOrEmpty();
        role.KubernetesGroups.Should().Contain("system:masters");
        output.WriteLine($"  {role.Name}: groups={string.Join(",", role.KubernetesGroups)}");
    }

    // ── Access requests ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccessRequest_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient();

        // Seed cluster + role
        var clusterName = $"cli-ar-cluster-{Guid.NewGuid().ToString()[..8]}";
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters", new { name = clusterName });
        var clusterJson = await clusterResp.Content.ReadAsStringAsync();
        var clusterId = JsonDocument.Parse(clusterJson).RootElement.GetProperty("clusterId").GetString()!;

        var roleName = $"cli-ar-role-{Guid.NewGuid().ToString()[..8]}";
        var roleResp = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = roleName,
            description = "Test",
            kubernetesGroups = new[] { "test-group" },
        });
        var roleJson = await roleResp.Content.ReadAsStringAsync();
        var roleId = JsonDocument.Parse(roleJson).RootElement.GetProperty("id").GetString()!;

        // Create access request — same payload the CLI sends
        var body = new AccessRequestCreateRequest
        {
            RoleId = roleId,
            ClusterId = clusterId,
            Reason = "CLI integration test",
        };
        var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestCreateRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/access-requests", content);

        output.WriteLine($"POST /api/v1/access-requests => {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var respJson = await response.Content.ReadAsStringAsync();
        var ar = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);

        ar.Should().NotBeNull();
        ar!.Id.Should().NotBeNullOrEmpty();
        ar.Status.Should().Be("Pending");
        ar.RoleName.Should().Be(roleName);
        ar.ClusterName.Should().Be(clusterName);
        ar.Reason.Should().Be("CLI integration test");
        output.WriteLine($"  Request {ar.Id[..8]}: {ar.Status} for {ar.RoleName} on {ar.ClusterName}");
    }

    [Fact]
    public async Task ListAccessRequests_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient(
            sub: "cli-list-ar-sub",
            email: "cli-list@test.com",
            name: "CLI List Tester");

        // Seed
        var clusterName = $"cli-list-cluster-{Guid.NewGuid().ToString()[..8]}";
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters", new { name = clusterName });
        var clusterId = JsonDocument.Parse(await clusterResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("clusterId").GetString()!;

        var roleName = $"cli-list-role-{Guid.NewGuid().ToString()[..8]}";
        var roleResp = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = roleName,
            description = "Test",
            kubernetesGroups = new[] { "test" },
        });
        var roleId = JsonDocument.Parse(await roleResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Create request
        var body = new AccessRequestCreateRequest { RoleId = roleId, ClusterId = clusterId };
        var json = JsonSerializer.Serialize(body, CliJsonContext.Default.AccessRequestCreateRequest);
        await client.PostAsync("/api/v1/access-requests",
            new StringContent(json, Encoding.UTF8, "application/json"));

        // List — same endpoint the CLI calls
        var response = await client.GetAsync("/api/v1/access-requests?mine=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var listJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize(listJson, CliJsonContext.Default.AccessRequestListResponse);

        output.WriteLine($"Access requests: {result!.Requests.Count}");
        result.Should().NotBeNull();
        result!.Requests.Should().NotBeEmpty();
        result.Requests.Should().Contain(r => r.ClusterName == clusterName);
    }

    [Fact]
    public async Task ApproveAccessRequest_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient(
            sub: "cli-approve-sub",
            email: "cli-approve@test.com",
            name: "CLI Approver");

        // Seed cluster + role + request
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters",
            new { name = $"cli-approve-{Guid.NewGuid().ToString()[..8]}" });
        var clusterId = JsonDocument.Parse(await clusterResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("clusterId").GetString()!;

        var roleResp = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = $"cli-approve-role-{Guid.NewGuid().ToString()[..8]}",
            description = "Test",
            kubernetesGroups = new[] { "test" },
        });
        var roleId = JsonDocument.Parse(await roleResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var createBody = new AccessRequestCreateRequest { RoleId = roleId, ClusterId = clusterId };
        var createJson = JsonSerializer.Serialize(createBody, CliJsonContext.Default.AccessRequestCreateRequest);
        var createResp = await client.PostAsync("/api/v1/access-requests",
            new StringContent(createJson, Encoding.UTF8, "application/json"));
        var requestId = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Approve — same payload the CLI sends
        var approveBody = new AccessRequestApproveRequest();
        var approveJson = JsonSerializer.Serialize(approveBody, CliJsonContext.Default.AccessRequestApproveRequest);
        var response = await client.PostAsync($"/api/v1/access-requests/{requestId}/approve",
            new StringContent(approveJson, Encoding.UTF8, "application/json"));

        output.WriteLine($"POST .../approve => {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var respJson = await response.Content.ReadAsStringAsync();
        var ar = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);

        ar.Should().NotBeNull();
        ar!.Status.Should().Be("Approved");
        ar.GrantExpiresAt.Should().NotBeNull();
        output.WriteLine($"  Approved, grant expires: {ar.GrantExpiresAt}");
    }

    [Fact]
    public async Task DenyAccessRequest_DeserializesWithCliWireType()
    {
        var client = factory.CreateAuthenticatedClient(
            sub: "cli-deny-sub",
            email: "cli-deny@test.com",
            name: "CLI Denier");

        // Seed
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters",
            new { name = $"cli-deny-{Guid.NewGuid().ToString()[..8]}" });
        var clusterId = JsonDocument.Parse(await clusterResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("clusterId").GetString()!;

        var roleResp = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name = $"cli-deny-role-{Guid.NewGuid().ToString()[..8]}",
            description = "Test",
            kubernetesGroups = new[] { "test" },
        });
        var roleId = JsonDocument.Parse(await roleResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var createBody = new AccessRequestCreateRequest { RoleId = roleId, ClusterId = clusterId };
        var createJson = JsonSerializer.Serialize(createBody, CliJsonContext.Default.AccessRequestCreateRequest);
        var createResp = await client.PostAsync("/api/v1/access-requests",
            new StringContent(createJson, Encoding.UTF8, "application/json"));
        var requestId = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Deny — same payload the CLI sends
        var denyBody = new AccessRequestDenyRequest { Reason = "Not authorized" };
        var denyJson = JsonSerializer.Serialize(denyBody, CliJsonContext.Default.AccessRequestDenyRequest);
        var response = await client.PostAsync($"/api/v1/access-requests/{requestId}/deny",
            new StringContent(denyJson, Encoding.UTF8, "application/json"));

        output.WriteLine($"POST .../deny => {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var respJson = await response.Content.ReadAsStringAsync();
        var ar = JsonSerializer.Deserialize(respJson, CliJsonContext.Default.AccessRequestResponse);

        ar.Should().NotBeNull();
        ar!.Status.Should().Be("Denied");
        ar.DenialReason.Should().Be("Not authorized");
        output.WriteLine($"  Denied: {ar.DenialReason}");
    }

    // ── Auth ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = factory.CreateClient(); // No auth header

        var response = await client.GetAsync("/api/v1/clusters");

        output.WriteLine($"GET /api/v1/clusters (no auth) => {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
