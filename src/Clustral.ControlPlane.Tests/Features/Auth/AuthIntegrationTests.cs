using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Auth;

[Collection(Integration.IntegrationTestCollection.Name)]
public sealed class AuthIntegrationTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    private async Task<string> CreateClusterAsync(HttpClient client)
    {
        var name = $"auth-{Guid.NewGuid():N}"[..20];
        var resp = await client.PostAsJsonAsync("/api/v1/clusters", new
        {
            name, description = "",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("clusterId").GetString()!;
    }

    [Fact]
    public async Task IssueCredential_Returns201_WithToken()
    {
        var client = factory.CreateAuthenticatedClient();
        var clusterId = await CreateClusterAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential",
            new { clusterId });

        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"Issue: {(int)response.StatusCode} {body[..Math.Min(200, body.Length)]}");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("credentialId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IssueCredential_NonexistentCluster_Returns404()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential",
            new { clusterId = Guid.NewGuid() });

        output.WriteLine($"Nonexistent cluster: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeCredentialById_Returns200()
    {
        var client = factory.CreateAuthenticatedClient();
        var clusterId = await CreateClusterAsync(client);

        // Issue.
        var issueResp = await client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential",
            new { clusterId });
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        var credentialId = issueDoc.RootElement.GetProperty("credentialId").GetString()!;

        // Revoke.
        var revokeResp = await client.DeleteAsync($"/api/v1/auth/credentials/{credentialId}");

        output.WriteLine($"Revoke by ID: {(int)revokeResp.StatusCode}");
        revokeResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeByToken_Returns200()
    {
        var client = factory.CreateAuthenticatedClient();
        var clusterId = await CreateClusterAsync(client);

        // Issue.
        var issueResp = await client.PostAsJsonAsync("/api/v1/auth/kubeconfig-credential",
            new { clusterId });
        var issueBody = await issueResp.Content.ReadAsStringAsync();
        using var issueDoc = JsonDocument.Parse(issueBody);
        var token = issueDoc.RootElement.GetProperty("token").GetString()!;

        // Revoke by token.
        var revokeResp = await client.PostAsJsonAsync("/api/v1/auth/revoke-by-token",
            new { token });

        output.WriteLine($"Revoke by token: {(int)revokeResp.StatusCode}");
        revokeResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeCredential_Nonexistent_Returns404()
    {
        var client = factory.CreateAuthenticatedClient();

        var response = await client.DeleteAsync($"/api/v1/auth/credentials/{Guid.NewGuid()}");

        output.WriteLine($"Revoke nonexistent: {(int)response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
