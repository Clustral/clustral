using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthApiTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private const string AuthUrl = "/api/v1/auth";
    private const string ClustersUrl = "/api/v1/clusters";

    [Fact]
    public async Task IssueKubeconfigCredential_Returns201_WithToken()
    {
        var client = factory.CreateAuthenticatedClient();

        // Create a cluster first.
        var clusterName = $"cluster-{Guid.NewGuid().ToString("N")[..8]}";
        var clusterRes = await client.PostAsJsonAsync(ClustersUrl, new { name = clusterName });
        using var clusterDoc = JsonDocument.Parse(await clusterRes.Content.ReadAsStringAsync());
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"{AuthUrl}/kubeconfig-credential",
            new { clusterId });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AuthUrl}/kubeconfig-credential => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("credentialId", out var credId));
        Assert.NotEqual(Guid.Empty, credId.GetGuid());
        Assert.True(doc.RootElement.TryGetProperty("token", out var tokenProp));
        Assert.False(string.IsNullOrEmpty(tokenProp.GetString()));
        Assert.True(doc.RootElement.TryGetProperty("expiresAt", out _));
    }

    [Fact]
    public async Task IssueCredential_NonexistentCluster_Returns404_WithProblemDetails()
    {
        var client = factory.CreateAuthenticatedClient();
        var fakeClusterId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"{AuthUrl}/kubeconfig-credential",
            new { clusterId = fakeClusterId });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AuthUrl}/kubeconfig-credential (404) => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task RevokeCredentialById_Returns200()
    {
        var client = factory.CreateAuthenticatedClient();

        // Create cluster + credential.
        var clusterName = $"cluster-{Guid.NewGuid().ToString("N")[..8]}";
        var clusterRes = await client.PostAsJsonAsync(ClustersUrl, new { name = clusterName });
        using var clusterDoc = JsonDocument.Parse(await clusterRes.Content.ReadAsStringAsync());
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetGuid();

        var issueRes = await client.PostAsJsonAsync(
            $"{AuthUrl}/kubeconfig-credential",
            new { clusterId });
        using var issueDoc = JsonDocument.Parse(await issueRes.Content.ReadAsStringAsync());
        var credentialId = issueDoc.RootElement.GetProperty("credentialId").GetGuid();

        // Revoke by ID.
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{AuthUrl}/credentials/{credentialId}")
        {
            Content = JsonContent.Create(new { reason = "test revocation" })
        };
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"DELETE {AuthUrl}/credentials/{credentialId} => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("revoked").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("revokedAt", out _));
    }

    [Fact]
    public async Task RevokeByToken_Returns200()
    {
        var client = factory.CreateAuthenticatedClient();

        // Create cluster + credential.
        var clusterName = $"cluster-{Guid.NewGuid().ToString("N")[..8]}";
        var clusterRes = await client.PostAsJsonAsync(ClustersUrl, new { name = clusterName });
        using var clusterDoc = JsonDocument.Parse(await clusterRes.Content.ReadAsStringAsync());
        var clusterId = clusterDoc.RootElement.GetProperty("clusterId").GetGuid();

        var issueRes = await client.PostAsJsonAsync(
            $"{AuthUrl}/kubeconfig-credential",
            new { clusterId });
        using var issueDoc = JsonDocument.Parse(await issueRes.Content.ReadAsStringAsync());
        var rawToken = issueDoc.RootElement.GetProperty("token").GetString()!;

        // Revoke by token.
        var response = await client.PostAsJsonAsync(
            $"{AuthUrl}/revoke-by-token",
            new { token = rawToken });
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"POST {AuthUrl}/revoke-by-token => {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("revoked").GetBoolean());
    }
}
