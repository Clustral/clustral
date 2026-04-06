using Clustral.ControlPlane.Api.Models;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

public class UserModelsTests(ITestOutputHelper output)
{
    [Fact]
    public void UserProfileResponse_WithAssignmentsAndGrants()
    {
        var profile = new UserProfileResponse(
            Id: Guid.NewGuid(),
            Email: "alice@example.com",
            DisplayName: "Alice",
            CreatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            LastSeenAt: DateTimeOffset.UtcNow,
            Assignments:
            [
                new RoleAssignmentResponse(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "admin",
                    Guid.NewGuid(), "production",
                    DateTimeOffset.UtcNow, "admin@example.com"),
            ],
            ActiveGrants:
            [
                new ActiveGrantResponse(
                    Guid.NewGuid(), "developer",
                    Guid.NewGuid(), "staging",
                    DateTimeOffset.UtcNow.AddHours(4)),
            ]);

        output.WriteLine($"=== UserProfileResponse ===");
        output.WriteLine($"  Email:        {profile.Email}");
        output.WriteLine($"  Assignments:  {profile.Assignments.Count}");
        output.WriteLine($"  ActiveGrants: {profile.ActiveGrants.Count}");
        foreach (var a in profile.Assignments)
            output.WriteLine($"    Static: {a.ClusterName} -> {a.RoleName}");
        foreach (var g in profile.ActiveGrants)
            output.WriteLine($"    JIT:    {g.ClusterName} -> {g.RoleName} (expires {g.GrantExpiresAt:HH:mm})");

        Assert.Equal("alice@example.com", profile.Email);
        Assert.Single(profile.Assignments);
        Assert.Single(profile.ActiveGrants);
    }

    [Fact]
    public void UserProfileResponse_EmptyAssignmentsAndGrants()
    {
        var profile = new UserProfileResponse(
            Guid.NewGuid(), "new@example.com", null,
            DateTimeOffset.UtcNow, null, [], []);

        output.WriteLine($"New user: {profile.Email}");
        output.WriteLine($"  Assignments:  {profile.Assignments.Count}");
        output.WriteLine($"  ActiveGrants: {profile.ActiveGrants.Count}");

        Assert.Empty(profile.Assignments);
        Assert.Empty(profile.ActiveGrants);
    }

    [Fact]
    public void RoleAssignmentResponse_AllFields()
    {
        var response = new RoleAssignmentResponse(
            Id: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            RoleId: Guid.NewGuid(),
            RoleName: "admin",
            ClusterId: Guid.NewGuid(),
            ClusterName: "production",
            AssignedAt: DateTimeOffset.UtcNow,
            AssignedBy: "admin@example.com");

        output.WriteLine($"Assignment: {response.ClusterName} -> {response.RoleName} by {response.AssignedBy}");

        Assert.Equal("admin", response.RoleName);
        Assert.Equal("production", response.ClusterName);
    }

    [Fact]
    public void AssignRoleRequest_RequiredFields()
    {
        var request = new AssignRoleRequest(
            RoleId: Guid.NewGuid(),
            ClusterId: Guid.NewGuid());

        output.WriteLine($"RoleId:    {request.RoleId}");
        output.WriteLine($"ClusterId: {request.ClusterId}");

        Assert.NotEqual(Guid.Empty, request.RoleId);
        Assert.NotEqual(Guid.Empty, request.ClusterId);
    }

    [Fact]
    public void UserResponse_AllFields()
    {
        var response = new UserResponse(
            Guid.NewGuid(), "alice@example.com", "Alice",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.UtcNow);

        output.WriteLine($"User: {response.Email} ({response.DisplayName})");

        Assert.Equal("alice@example.com", response.Email);
        Assert.Equal("Alice", response.DisplayName);
    }

    [Fact]
    public void UserResponse_NullOptionals()
    {
        var response = new UserResponse(
            Guid.NewGuid(), "noname@example.com", null,
            DateTimeOffset.UtcNow, null);

        output.WriteLine($"User: {response.Email} (displayName: null, lastSeen: null)");

        Assert.Null(response.DisplayName);
        Assert.Null(response.LastSeenAt);
    }
}
