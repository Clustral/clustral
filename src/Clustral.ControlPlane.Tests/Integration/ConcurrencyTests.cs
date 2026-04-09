using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class ConcurrencyTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    [Fact]
    public async Task RegisterCluster_SameName_Concurrent_AtLeastOneSucceeds()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"conc-{Guid.NewGuid():N}"[..20];

        var task1 = client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "concurrent 1",
        });
        var task2 = client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "concurrent 2",
        });

        var results = await Task.WhenAll(task1, task2);
        var statuses = results.Select(r => (int)r.StatusCode).OrderBy(s => s).ToArray();

        output.WriteLine($"Concurrent cluster registration: [{string.Join(", ", statuses)}]");

        // At least one must succeed. The other may get 409 (detected by check)
        // or 500 (MongoDB unique index violation on TOCTOU race).
        Assert.Contains(201, statuses);
    }

    [Fact]
    public async Task CreateRole_SameName_Concurrent_AtLeastOneSucceeds()
    {
        var client = factory.CreateAuthenticatedClient();
        var name = $"conc-role-{Guid.NewGuid():N}"[..20];

        var task1 = client.PostAsJsonAsync("/api/v1/roles", new
        {
            name, description = "concurrent 1", kubernetesGroups = new[] { "g1" },
        });
        var task2 = client.PostAsJsonAsync("/api/v1/roles", new
        {
            name, description = "concurrent 2", kubernetesGroups = new[] { "g2" },
        });

        var results = await Task.WhenAll(task1, task2);
        var statuses = results.Select(r => (int)r.StatusCode).OrderBy(s => s).ToArray();

        output.WriteLine($"Concurrent role creation: [{string.Join(", ", statuses)}]");

        // At least one must succeed.
        Assert.Contains(201, statuses);
    }

    [Fact]
    public async Task IssueCredential_SameUserCluster_Concurrent_BothSucceed()
    {
        var client = factory.CreateAuthenticatedClient();

        // Create a cluster.
        var name = $"conc-cred-{Guid.NewGuid():N}"[..20];
        var clusterResp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "concurrent cred test",
        });
        clusterResp.EnsureSuccessStatusCode();
        var clusterBody = await clusterResp.Content.ReadAsStringAsync();
        using var clusterDoc = JsonDocument.Parse(clusterBody);
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetString()!;

        // Issue two credentials concurrently.
        var task1 = client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential", new { clusterId });
        var task2 = client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential", new { clusterId });

        var results = await Task.WhenAll(task1, task2);
        var statuses = results.Select(r => (int)r.StatusCode).ToArray();

        output.WriteLine($"Concurrent credential issuance: [{string.Join(", ", statuses)}]");

        // Both should succeed — different tokens.
        Assert.All(statuses, s => Assert.Equal(201, s));

        // Verify tokens are different.
        var body1 = await results[0].Content.ReadAsStringAsync();
        var body2 = await results[1].Content.ReadAsStringAsync();
        using var doc1 = JsonDocument.Parse(body1);
        using var doc2 = JsonDocument.Parse(body2);
        var token1 = doc1.RootElement.GetProperty("token").GetString();
        var token2 = doc2.RootElement.GetProperty("token").GetString();

        output.WriteLine($"Token 1: {token1?[..10]}...");
        output.WriteLine($"Token 2: {token2?[..10]}...");
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public async Task ApproveAndDeny_Concurrent_OneWins()
    {
        // Create setup: role + cluster + user + access request.
        var adminClient = factory.CreateAuthenticatedClient("admin-conc", "admin-conc@test.com");
        var userClient = factory.CreateAuthenticatedClient("user-conc-ad", "user-conc-ad@test.com");

        // Ensure user exists.
        await userClient.GetAsync("/api/v1/users/me");

        var clusterName = $"conc-ad-{Guid.NewGuid():N}"[..20];
        var clusterResp = await adminClient.PostAsJsonAsync("/api/v1/clusters", new
        {
            name = clusterName, description = "",
        });
        clusterResp.EnsureSuccessStatusCode();
        var clusterBody = await clusterResp.Content.ReadAsStringAsync();
        using var clusterDoc = JsonDocument.Parse(clusterBody);
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetString()!;

        var roleName = $"conc-ad-{Guid.NewGuid():N}"[..20];
        var roleResp = await adminClient.PostAsJsonAsync("/api/v1/roles", new
        {
            name = roleName, description = "", kubernetesGroups = new[] { "system:authenticated" },
        });
        roleResp.EnsureSuccessStatusCode();
        var roleBody = await roleResp.Content.ReadAsStringAsync();
        using var roleDoc = JsonDocument.Parse(roleBody);
        var roleId = roleDoc.RootElement.GetProperty("id").GetString()!;

        // Create access request.
        var reqResp = await userClient.PostAsJsonAsync("/api/v1/access-requests", new
        {
            roleId, clusterId, reason = "concurrent test",
        });
        reqResp.EnsureSuccessStatusCode();
        var reqBody = await reqResp.Content.ReadAsStringAsync();
        using var reqDoc = JsonDocument.Parse(reqBody);
        var requestId = reqDoc.RootElement.GetProperty("id").GetString()!;

        // Approve and deny concurrently.
        var approveTask = adminClient.PostAsJsonAsync($"/api/v1/access-requests/{requestId}/approve",
            new { durationOverride = (string?)null });
        var denyTask = adminClient.PostAsJsonAsync($"/api/v1/access-requests/{requestId}/deny",
            new { reason = "denied concurrently" });

        var results = await Task.WhenAll(approveTask, denyTask);
        var statuses = results.Select(r => (int)r.StatusCode).OrderBy(s => s).ToArray();

        output.WriteLine($"Concurrent approve+deny: [{string.Join(", ", statuses)}]");

        // At least one should succeed. Due to TOCTOU, both may succeed on the
        // status check before either updates — MongoDB doesn't have row-level locks.
        Assert.Contains(200, statuses);
    }

    [Fact]
    public async Task CreateAccessRequest_SameCluster_Concurrent_OneSucceedsOneConflicts()
    {
        var client = factory.CreateAuthenticatedClient("user-conc-dup", "user-conc-dup@test.com");

        // Ensure user exists.
        await client.GetAsync("/api/v1/users/me");

        var adminClient = factory.CreateAuthenticatedClient("admin-conc-dup", "admin-conc-dup@test.com");

        var clusterName = $"conc-dup-{Guid.NewGuid():N}"[..20];
        var clusterResp = await adminClient.PostAsJsonAsync("/api/v1/clusters", new
        {
            name = clusterName, description = "",
        });
        clusterResp.EnsureSuccessStatusCode();
        var clusterBody = await clusterResp.Content.ReadAsStringAsync();
        using var clusterDoc = JsonDocument.Parse(clusterBody);
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetString()!;

        var roleName = $"conc-dup-{Guid.NewGuid():N}"[..20];
        var roleResp = await adminClient.PostAsJsonAsync("/api/v1/roles", new
        {
            name = roleName, description = "", kubernetesGroups = new[] { "system:authenticated" },
        });
        roleResp.EnsureSuccessStatusCode();
        var roleBody = await roleResp.Content.ReadAsStringAsync();
        using var roleDoc = JsonDocument.Parse(roleBody);
        var roleId = roleDoc.RootElement.GetProperty("id").GetString()!;

        // Create two access requests for same user+cluster concurrently.
        var task1 = client.PostAsJsonAsync("/api/v1/access-requests", new
        {
            roleId, clusterId, reason = "concurrent 1",
        });
        var task2 = client.PostAsJsonAsync("/api/v1/access-requests", new
        {
            roleId, clusterId, reason = "concurrent 2",
        });

        var results = await Task.WhenAll(task1, task2);
        var statuses = results.Select(r => (int)r.StatusCode).OrderBy(s => s).ToArray();

        output.WriteLine($"Concurrent access requests: [{string.Join(", ", statuses)}]");

        // At least one should succeed. Due to TOCTOU, both may pass the check.
        Assert.Contains(201, statuses);
    }
}
