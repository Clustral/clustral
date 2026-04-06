using System.Text.Json;
using Clustral.Cli.Config;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class AccessRequestWireTypeTests(ITestOutputHelper output)
{
    [Fact]
    public void AccessRequestCreateRequest_Serializes()
    {
        var req = new AccessRequestCreateRequest
        {
            RoleId = "role-123",
            ClusterId = "cluster-456",
            Reason = "deploy hotfix",
            RequestedDuration = "PT4H",
            SuggestedReviewerEmails = ["alice@example.com", "bob@example.com"],
        };

        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.AccessRequestCreateRequest);

        output.WriteLine("=== AccessRequestCreateRequest ===");
        output.WriteLine(json);

        Assert.Contains("\"roleId\"", json);
        Assert.Contains("\"clusterId\"", json);
        Assert.Contains("\"reason\"", json);
        Assert.Contains("\"requestedDuration\"", json);
        Assert.Contains("\"suggestedReviewerEmails\"", json);
        Assert.Contains("alice@example.com", json);
    }

    [Fact]
    public void AccessRequestCreateRequest_NullOptionals_Omitted()
    {
        var req = new AccessRequestCreateRequest
        {
            RoleId = "r1",
            ClusterId = "c1",
        };

        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.AccessRequestCreateRequest);

        output.WriteLine(json);

        Assert.DoesNotContain("reason", json);
        Assert.DoesNotContain("requestedDuration", json);
        Assert.DoesNotContain("suggestedReviewerEmails", json);
    }

    [Fact]
    public void AccessRequestResponse_Deserializes()
    {
        var json = """
        {
            "id": "req-abc",
            "requesterId": "user-1",
            "requesterEmail": "alice@example.com",
            "requesterDisplayName": "Alice",
            "roleId": "role-1",
            "roleName": "admin",
            "clusterId": "cluster-1",
            "clusterName": "production",
            "status": "Pending",
            "reason": "deploy hotfix",
            "requestedDuration": "04:00:00",
            "createdAt": "2026-04-06T10:00:00Z",
            "requestExpiresAt": "2026-04-06T11:00:00Z",
            "reviewerId": null,
            "reviewerEmail": null,
            "reviewedAt": null,
            "denialReason": null,
            "grantExpiresAt": null
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.AccessRequestResponse);

        output.WriteLine("=== Pending AccessRequestResponse ===");
        output.WriteLine($"  ID:        {resp!.Id}");
        output.WriteLine($"  Role:      {resp.RoleName}");
        output.WriteLine($"  Cluster:   {resp.ClusterName}");
        output.WriteLine($"  Status:    {resp.Status}");
        output.WriteLine($"  Requester: {resp.RequesterEmail}");

        Assert.Equal("Pending", resp.Status);
        Assert.Equal("admin", resp.RoleName);
        Assert.Null(resp.GrantExpiresAt);
    }

    [Fact]
    public void AccessRequestResponse_Approved_Deserializes()
    {
        var json = """
        {
            "id": "req-def",
            "requesterId": "user-1",
            "requesterEmail": "alice@example.com",
            "roleId": "role-1",
            "roleName": "admin",
            "clusterId": "cluster-1",
            "clusterName": "production",
            "status": "Approved",
            "reason": "deploy",
            "requestedDuration": "04:00:00",
            "createdAt": "2026-04-06T10:00:00Z",
            "requestExpiresAt": "2026-04-06T11:00:00Z",
            "reviewerId": "user-2",
            "reviewerEmail": "bob@example.com",
            "reviewedAt": "2026-04-06T10:05:00Z",
            "grantExpiresAt": "2026-04-06T14:05:00Z"
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.AccessRequestResponse);

        output.WriteLine("=== Approved AccessRequestResponse ===");
        output.WriteLine($"  Reviewer:     {resp!.ReviewerEmail}");
        output.WriteLine($"  GrantExpires: {resp.GrantExpiresAt}");

        Assert.Equal("Approved", resp.Status);
        Assert.Equal("bob@example.com", resp.ReviewerEmail);
        Assert.NotNull(resp.GrantExpiresAt);
    }

    [Fact]
    public void AccessRequestListResponse_Deserializes()
    {
        var json = """
        {
            "requests": [
                {
                    "id": "r1",
                    "requesterId": "u1",
                    "requesterEmail": "a@b.com",
                    "roleName": "admin",
                    "clusterId": "c1",
                    "clusterName": "prod",
                    "status": "Pending",
                    "reason": "",
                    "requestedDuration": "08:00:00",
                    "createdAt": "2026-04-06T10:00:00Z",
                    "requestExpiresAt": "2026-04-06T11:00:00Z"
                }
            ]
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.AccessRequestListResponse);

        output.WriteLine($"Requests count: {resp!.Requests.Count}");

        Assert.Single(resp.Requests);
    }

    [Fact]
    public void AccessRequestApproveRequest_Serializes()
    {
        var req = new AccessRequestApproveRequest { DurationOverride = "PT2H" };
        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.AccessRequestApproveRequest);

        output.WriteLine(json);

        Assert.Contains("\"durationOverride\"", json);
    }

    [Fact]
    public void AccessRequestDenyRequest_Serializes()
    {
        var req = new AccessRequestDenyRequest { Reason = "Not authorized" };
        var json = JsonSerializer.Serialize(req, CliJsonContext.Default.AccessRequestDenyRequest);

        output.WriteLine(json);

        Assert.Contains("\"reason\"", json);
        Assert.Contains("Not authorized", json);
    }

    [Fact]
    public void ActiveGrant_Deserializes()
    {
        var json = """
        {
            "requestId": "grant-1",
            "roleName": "admin",
            "clusterId": "c1",
            "clusterName": "production",
            "grantExpiresAt": "2026-04-06T18:00:00Z"
        }
        """;

        var grant = JsonSerializer.Deserialize<ActiveGrant>(json, CliJsonContext.Default.Options);

        output.WriteLine($"Grant: {grant!.RoleName} on {grant.ClusterName}, expires {grant.GrantExpiresAt}");

        Assert.Equal("admin", grant.RoleName);
        Assert.Equal("production", grant.ClusterName);
        Assert.Equal(2026, grant.GrantExpiresAt.Year);
    }

    [Fact]
    public void UserListResponse_Deserializes()
    {
        var json = """
        {
            "users": [
                { "id": "u1", "email": "alice@example.com", "displayName": "Alice", "lastSeenAt": "2026-04-06T10:00:00Z" },
                { "id": "u2", "email": "bob@example.com", "displayName": null, "lastSeenAt": null }
            ]
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserListResponse);

        output.WriteLine($"Users: {resp!.Users.Count}");
        foreach (var u in resp.Users)
            output.WriteLine($"  {u.Email} ({u.DisplayName ?? "no name"})");

        Assert.Equal(2, resp.Users.Count);
        Assert.Null(resp.Users[1].DisplayName);
        Assert.Null(resp.Users[1].LastSeenAt);
    }

    [Fact]
    public void UserProfileResponse_WithActiveGrants()
    {
        var json = """
        {
            "id": "u1",
            "email": "alice@example.com",
            "displayName": "Alice",
            "createdAt": "2026-01-01T00:00:00Z",
            "assignments": [],
            "activeGrants": [
                {
                    "requestId": "g1",
                    "roleName": "admin",
                    "clusterId": "c1",
                    "clusterName": "production",
                    "grantExpiresAt": "2026-04-06T18:00:00Z"
                }
            ]
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserProfileResponse);

        output.WriteLine($"User: {resp!.Email}");
        output.WriteLine($"Assignments: {resp.Assignments.Count}");
        output.WriteLine($"Active grants: {resp.ActiveGrants.Count}");
        foreach (var g in resp.ActiveGrants)
            output.WriteLine($"  {g.ClusterName} -> {g.RoleName} (expires {g.GrantExpiresAt})");

        Assert.Single(resp.ActiveGrants);
        Assert.Equal("admin", resp.ActiveGrants[0].RoleName);
    }
}
