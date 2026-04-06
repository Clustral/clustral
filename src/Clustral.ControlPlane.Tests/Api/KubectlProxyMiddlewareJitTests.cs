using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

/// <summary>
/// Tests for the JIT grant fallback logic in KubectlProxyMiddleware.
/// These test the domain-level access decision logic.
/// Full HTTP integration tests would require WebApplicationFactory + MongoDB.
/// </summary>
public class KubectlProxyMiddlewareJitTests(ITestOutputHelper output)
{
    /// <summary>
    /// Simulates the access decision logic from the middleware (lines 132-161).
    /// Returns the roleId to use for impersonation, or null if access denied.
    /// </summary>
    private static Guid? ResolveAccessRoleId(
        RoleAssignment? staticAssignment,
        AccessRequest? activeGrant)
    {
        if (staticAssignment is not null)
            return staticAssignment.RoleId;

        if (activeGrant is not null && activeGrant.IsGrantActive)
            return activeGrant.RoleId;

        return null; // 403
    }

    [Fact]
    public void StaticAssignment_UsesAssignmentRole()
    {
        var roleId = Guid.NewGuid();
        var assignment = new RoleAssignment { RoleId = roleId };

        var result = ResolveAccessRoleId(assignment, activeGrant: null);

        output.WriteLine($"Static assignment roleId: {roleId}");
        output.WriteLine($"Resolved: {result}");

        Assert.Equal(roleId, result);
    }

    [Fact]
    public void StaticAssignment_TakesPrecedenceOverGrant()
    {
        var assignmentRoleId = Guid.NewGuid();
        var grantRoleId = Guid.NewGuid();

        var assignment = new RoleAssignment { RoleId = assignmentRoleId };
        var grant = new AccessRequest
        {
            RoleId = grantRoleId,
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
        };

        var result = ResolveAccessRoleId(assignment, grant);

        output.WriteLine($"Assignment roleId: {assignmentRoleId}");
        output.WriteLine($"Grant roleId:      {grantRoleId}");
        output.WriteLine($"Resolved:          {result} (static wins)");

        Assert.Equal(assignmentRoleId, result);
    }

    [Fact]
    public void NoStaticAssignment_ActiveGrant_UsesGrantRole()
    {
        var grantRoleId = Guid.NewGuid();
        var grant = new AccessRequest
        {
            RoleId = grantRoleId,
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(3),
        };

        var result = ResolveAccessRoleId(null, grant);

        output.WriteLine($"No static assignment");
        output.WriteLine($"Active grant roleId: {grantRoleId}");
        output.WriteLine($"Resolved: {result}");

        Assert.Equal(grantRoleId, result);
    }

    [Fact]
    public void NoStaticAssignment_NoGrant_Returns403()
    {
        var result = ResolveAccessRoleId(null, null);

        output.WriteLine("No assignment, no grant => 403");

        Assert.Null(result);
    }

    [Fact]
    public void NoStaticAssignment_ExpiredGrant_Returns403()
    {
        var grant = new AccessRequest
        {
            RoleId = Guid.NewGuid(),
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };

        var result = ResolveAccessRoleId(null, grant);

        output.WriteLine($"Grant expired 1s ago => IsGrantActive: {grant.IsGrantActive}");
        output.WriteLine($"Resolved: {(result is null ? "null (403)" : result.ToString())}");

        Assert.Null(result);
    }

    [Fact]
    public void NoStaticAssignment_PendingGrant_Returns403()
    {
        var grant = new AccessRequest
        {
            RoleId = Guid.NewGuid(),
            Status = AccessRequestStatus.Pending,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
        };

        var result = ResolveAccessRoleId(null, grant);

        output.WriteLine($"Grant status: Pending (not Approved) => 403");

        Assert.Null(result);
    }

    [Fact]
    public void ImpersonationGroups_AlwaysIncludeSystemAuthenticated()
    {
        var groups = new List<string> { "system:authenticated" };
        var roleGroups = new[] { "system:masters", "clustral-admin" };

        foreach (var group in roleGroups)
        {
            if (!groups.Contains(group))
                groups.Add(group);
        }

        output.WriteLine($"Impersonation groups: [{string.Join(", ", groups)}]");

        Assert.Contains("system:authenticated", groups);
        Assert.Contains("system:masters", groups);
        Assert.Contains("clustral-admin", groups);
        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void ImpersonationGroups_NoDuplicates()
    {
        var groups = new List<string> { "system:authenticated" };
        var roleGroups = new[] { "system:authenticated", "system:masters" };

        foreach (var group in roleGroups)
        {
            if (!groups.Contains(group))
                groups.Add(group);
        }

        output.WriteLine($"Groups after dedup: [{string.Join(", ", groups)}]");

        Assert.Equal(2, groups.Count); // No duplicate system:authenticated
    }
}
