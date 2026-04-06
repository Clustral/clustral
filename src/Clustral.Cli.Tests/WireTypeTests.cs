using System.Text.Json;
using Clustral.Cli.Config;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

/// <summary>
/// Tests JSON serialization/deserialization of CLI wire types used in
/// ControlPlane API communication.
/// </summary>
public class WireTypeTests(ITestOutputHelper output)
{
    [Fact]
    public void IssueCredentialRequest_Serializes()
    {
        var req = new IssueCredentialRequest
        {
            ClusterId = "prod-cluster",
            RequestedTtl = "PT8H",
        };

        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.IssueCredentialRequest);

        output.WriteLine("=== IssueCredentialRequest ===");
        output.WriteLine(json);

        Assert.Contains("\"clusterId\"", json);
        Assert.Contains("\"prod-cluster\"", json);
        Assert.Contains("\"requestedTtl\"", json);
        Assert.Contains("\"PT8H\"", json);
    }

    [Fact]
    public void IssueCredentialRequest_NullTtl_OmittedInJson()
    {
        var req = new IssueCredentialRequest
        {
            ClusterId = "test",
            RequestedTtl = null,
        };

        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.IssueCredentialRequest);

        output.WriteLine("=== Null TTL ===");
        output.WriteLine(json);

        Assert.DoesNotContain("requestedTtl", json);
    }

    [Fact]
    public void IssueCredentialResponse_Deserializes()
    {
        var json = """
        {
            "credentialId": "cred-123",
            "token": "kube-token-abc",
            "issuedAt": "2026-04-06T10:00:00Z",
            "expiresAt": "2026-04-06T18:00:00Z",
            "subject": "user@example.com",
            "displayName": "Test User"
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.IssueCredentialResponse);

        output.WriteLine("=== IssueCredentialResponse ===");
        output.WriteLine($"  CredentialId: {resp!.CredentialId}");
        output.WriteLine($"  Token:        {resp.Token}");
        output.WriteLine($"  IssuedAt:     {resp.IssuedAt}");
        output.WriteLine($"  ExpiresAt:    {resp.ExpiresAt}");
        output.WriteLine($"  Subject:      {resp.Subject}");
        output.WriteLine($"  DisplayName:  {resp.DisplayName}");

        Assert.NotNull(resp);
        Assert.Equal("cred-123", resp.CredentialId);
        Assert.Equal("kube-token-abc", resp.Token);
        Assert.Equal("Test User", resp.DisplayName);
        Assert.Equal(2026, resp.ExpiresAt.Year);
    }

    [Fact]
    public void ClusterListResponse_Deserializes()
    {
        var json = """
        {
            "clusters": [
                {
                    "id": "c1",
                    "name": "production",
                    "status": "Connected",
                    "kubernetesVersion": "1.30.1",
                    "registeredAt": "2026-01-01T00:00:00Z",
                    "lastSeenAt": "2026-04-06T10:00:00Z",
                    "labels": { "env": "prod", "region": "eu-west-1" }
                },
                {
                    "id": "c2",
                    "name": "staging",
                    "status": "Pending",
                    "registeredAt": "2026-03-15T00:00:00Z",
                    "labels": {}
                }
            ]
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.ClusterListResponse);

        output.WriteLine("=== ClusterListResponse ===");
        foreach (var c in resp!.Clusters)
        {
            output.WriteLine($"  [{c.Id}] {c.Name} — {c.Status} — k8s {c.KubernetesVersion ?? "-"} — last seen {c.LastSeenAt?.ToString("u") ?? "-"}");
            if (c.Labels.Count > 0)
                output.WriteLine($"    labels: {string.Join(", ", c.Labels.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        Assert.NotNull(resp);
        Assert.Equal(2, resp.Clusters.Count);

        var prod = resp.Clusters[0];
        Assert.Equal("c1", prod.Id);
        Assert.Equal("production", prod.Name);
        Assert.Equal("Connected", prod.Status);
        Assert.Equal("1.30.1", prod.KubernetesVersion);
        Assert.Equal(2, prod.Labels.Count);
        Assert.Equal("prod", prod.Labels["env"]);

        var staging = resp.Clusters[1];
        Assert.Equal("Pending", staging.Status);
        Assert.Null(staging.KubernetesVersion);
        Assert.Null(staging.LastSeenAt);
    }

    [Fact]
    public void UserProfileResponse_Deserializes()
    {
        var json = """
        {
            "id": "user-1",
            "email": "admin@clustral.local",
            "displayName": "Admin User",
            "createdAt": "2026-01-01T00:00:00Z",
            "assignments": [
                { "roleName": "admin", "clusterName": "production", "clusterId": "c1" },
                { "roleName": "read-only", "clusterName": "staging", "clusterId": "c2" }
            ]
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserProfileResponse);

        output.WriteLine("=== UserProfileResponse ===");
        output.WriteLine($"  Email:       {resp!.Email}");
        output.WriteLine($"  DisplayName: {resp.DisplayName}");
        output.WriteLine($"  Assignments:");
        foreach (var a in resp.Assignments)
            output.WriteLine($"    {a.ClusterName} -> {a.RoleName}");

        Assert.NotNull(resp);
        Assert.Equal("admin@clustral.local", resp.Email);
        Assert.Equal("Admin User", resp.DisplayName);
        Assert.Equal(2, resp.Assignments.Count);
        Assert.Equal("admin", resp.Assignments[0].RoleName);
        Assert.Equal("production", resp.Assignments[0].ClusterName);
    }

    [Fact]
    public void ControlPlaneConfig_Deserializes()
    {
        var json = """
        {
            "oidcAuthority": "http://keycloak:8080/realms/clustral",
            "oidcClientId": "clustral-cli",
            "oidcScopes": "openid email profile"
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.ControlPlaneConfig);

        output.WriteLine("=== ControlPlaneConfig ===");
        output.WriteLine($"  OidcAuthority: {resp!.OidcAuthority}");
        output.WriteLine($"  OidcClientId:  {resp.OidcClientId}");
        output.WriteLine($"  OidcScopes:    {resp.OidcScopes}");

        Assert.NotNull(resp);
        Assert.Equal("http://keycloak:8080/realms/clustral", resp.OidcAuthority);
        Assert.Equal("clustral-cli", resp.OidcClientId);
        Assert.Equal("openid email profile", resp.OidcScopes);
    }

    [Fact]
    public void KeycloakTokenResponse_Deserializes()
    {
        var json = """
        {
            "access_token": "eyJhbGciOiJSUzI1NiJ9.test.sig",
            "expires_in": 300,
            "refresh_token": "refresh-abc",
            "token_type": "Bearer",
            "scope": "openid email profile"
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.KeycloakTokenResponse);

        output.WriteLine("=== KeycloakTokenResponse ===");
        output.WriteLine($"  AccessToken:  {resp!.AccessToken}");
        output.WriteLine($"  ExpiresIn:    {resp.ExpiresIn}s");
        output.WriteLine($"  TokenType:    {resp.TokenType}");
        output.WriteLine($"  Scope:        {resp.Scope}");

        Assert.NotNull(resp);
        Assert.Equal("eyJhbGciOiJSUzI1NiJ9.test.sig", resp.AccessToken);
        Assert.Equal(300, resp.ExpiresIn);
        Assert.Equal("Bearer", resp.TokenType);
    }
}
