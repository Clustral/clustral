using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class CascadeDeletionTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private async Task<(string ClusterId, string BootstrapToken)> CreateClusterAsync(HttpClient client)
    {
        var name = $"cascade-{Guid.NewGuid():N}"[..20];
        var resp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "cascade test",
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return (doc.RootElement.GetProperty("clusterId").GetString()!,
                doc.RootElement.GetProperty("bootstrapToken").GetString()!);
    }

    private async Task<string> CreateRoleAsync(HttpClient client)
    {
        var name = $"cascade-{Guid.NewGuid():N}"[..20];
        var resp = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name, description = "cascade test", kubernetesGroups = new[] { "system:authenticated" },
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> GetUserId(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/users/me");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task DeleteCluster_CascadesAccessTokens()
    {
        var client = factory.CreateAuthenticatedClient();
        var (clusterId, _) = await CreateClusterAsync(client);

        // Issue a credential for this cluster.
        var issueResp = await client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential",
            new { clusterId });
        issueResp.EnsureSuccessStatusCode();
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        var credentialId = issueDoc.RootElement.GetProperty("credentialId").GetString()!;
        var rawToken = issueDoc.RootElement.GetProperty("token").GetString()!;

        output.WriteLine($"Issued credential {credentialId} for cluster {clusterId}");

        // Delete the cluster.
        var deleteResp = await client.DeleteAsync($"/api/v1/clusters/{clusterId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
        output.WriteLine("Cluster deleted");

        // Try to revoke the credential — should be 404 (cascade deleted).
        var revokeResp = await client.DeleteAsync($"/api/v1/auth/credentials/{credentialId}");
        output.WriteLine($"Revoke after cascade: {(int)revokeResp.StatusCode}");
        Assert.Equal(HttpStatusCode.NotFound, revokeResp.StatusCode);
    }

    [Fact]
    public async Task DeleteRole_CascadesRoleAssignments()
    {
        var client = factory.CreateAuthenticatedClient();
        var roleId = await CreateRoleAsync(client);
        var (clusterId, _) = await CreateClusterAsync(client);
        var userId = await GetUserId(client);

        // Assign role to user.
        var assignResp = await client.PostAsJsonAsync($"/api/v1/users/{userId}/assignments",
            new { roleId, clusterId });
        assignResp.EnsureSuccessStatusCode();
        output.WriteLine($"Assigned role {roleId} to user {userId} on cluster {clusterId}");

        // Delete the role.
        var deleteResp = await client.DeleteAsync($"/api/v1/roles/{roleId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
        output.WriteLine("Role deleted");

        // Check assignments — should be empty (cascaded).
        var assignmentsResp = await client.GetAsync($"/api/v1/users/{userId}/assignments");
        assignmentsResp.EnsureSuccessStatusCode();
        var body = await assignmentsResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var assignments = doc.RootElement.GetProperty("assignments");

        // Filter for the deleted role — should not exist.
        var remaining = assignments.EnumerateArray()
            .Where(a => a.GetProperty("roleId").GetString() == roleId)
            .ToList();

        output.WriteLine($"Assignments with deleted role: {remaining.Count}");
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteAssignment_VerifiedGone()
    {
        var client = factory.CreateAuthenticatedClient();
        var roleId = await CreateRoleAsync(client);
        var (clusterId, _) = await CreateClusterAsync(client);
        var userId = await GetUserId(client);

        // Assign.
        var assignResp = await client.PostAsJsonAsync($"/api/v1/users/{userId}/assignments",
            new { roleId, clusterId });
        assignResp.EnsureSuccessStatusCode();
        var assignBody = await assignResp.Content.ReadAsStringAsync();
        using var assignDoc = JsonDocument.Parse(assignBody);
        var assignmentId = assignDoc.RootElement.GetProperty("id").GetString()!;

        output.WriteLine($"Created assignment {assignmentId}");

        // Delete.
        var deleteResp = await client.DeleteAsync($"/api/v1/users/{userId}/assignments/{assignmentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // Verify gone.
        var listResp = await client.GetAsync($"/api/v1/users/{userId}/assignments");
        var listBody = await listResp.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listBody);
        var remaining = listDoc.RootElement.GetProperty("assignments").EnumerateArray()
            .Where(a => a.GetProperty("id").GetString() == assignmentId)
            .ToList();

        output.WriteLine($"Assignment after delete: {remaining.Count}");
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteCluster_CredentialIssue_Returns404()
    {
        var client = factory.CreateAuthenticatedClient();
        var (clusterId, _) = await CreateClusterAsync(client);

        // Delete cluster.
        var deleteResp = await client.DeleteAsync($"/api/v1/clusters/{clusterId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // Try to issue credential for deleted cluster.
        var issueResp = await client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential",
            new { clusterId });

        output.WriteLine($"Issue credential for deleted cluster: {(int)issueResp.StatusCode}");
        Assert.Equal(HttpStatusCode.NotFound, issueResp.StatusCode);
    }

    [Fact]
    public async Task DeleteCluster_AfterCredentialRevoke_NoError()
    {
        var client = factory.CreateAuthenticatedClient();
        var (clusterId, _) = await CreateClusterAsync(client);

        // Issue credential.
        var issueResp = await client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential",
            new { clusterId });
        issueResp.EnsureSuccessStatusCode();
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        var token = issueDoc.RootElement.GetProperty("token").GetString()!;

        // Revoke credential.
        var revokeResp = await client.PostAsJsonAsync("/api/v1/auth/revoke-by-token",
            new { token });
        Assert.Equal(HttpStatusCode.OK, revokeResp.StatusCode);

        // Delete cluster — should work fine.
        var deleteResp = await client.DeleteAsync($"/api/v1/clusters/{clusterId}");
        output.WriteLine($"Delete after revoke: {(int)deleteResp.StatusCode}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }
}
