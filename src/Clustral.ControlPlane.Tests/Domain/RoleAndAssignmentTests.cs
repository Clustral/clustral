using Clustral.ControlPlane.Domain;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public sealed class RoleAndAssignmentTests(ITestOutputHelper output)
{
    // ── Role ────────────────────────────────────────────────────────────────

    [Fact]
    public void Role_Create_SetsProperties()
    {
        var role = Role.Create("k8s-admin", "Full admin", ["system:masters"]);

        output.WriteLine($"Role: {role.Name}, Groups: {string.Join(",", role.KubernetesGroups)}");
        role.Id.Should().NotBe(Guid.Empty);
        role.Name.Should().Be("k8s-admin");
        role.Description.Should().Be("Full admin");
        role.KubernetesGroups.Should().ContainSingle("system:masters");
        role.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Role_Create_NullGroups_DefaultsToEmpty()
    {
        var role = Role.Create("viewer", "Read-only");

        role.KubernetesGroups.Should().BeEmpty();
    }

    [Fact]
    public void Role_Update_PartialName()
    {
        var role = Role.Create("old-name", "desc", ["group1"]);

        role.Update("new-name", null, null);

        role.Name.Should().Be("new-name");
        role.Description.Should().Be("desc"); // unchanged
        role.KubernetesGroups.Should().ContainSingle("group1"); // unchanged
    }

    [Fact]
    public void Role_Update_PartialGroups()
    {
        var role = Role.Create("admin", "Admin role", ["system:masters"]);

        role.Update(null, null, ["system:masters", "cluster-admin"]);

        output.WriteLine($"Groups: {string.Join(",", role.KubernetesGroups)}");
        role.Name.Should().Be("admin"); // unchanged
        role.KubernetesGroups.Should().HaveCount(2);
    }

    [Fact]
    public void Role_Update_AllFields()
    {
        var role = Role.Create("old", "old desc", ["old-group"]);

        role.Update("new", "new desc", ["new-group"]);

        role.Name.Should().Be("new");
        role.Description.Should().Be("new desc");
        role.KubernetesGroups.Should().ContainSingle("new-group");
    }

    // ── RoleAssignment ──────────────────────────────────────────────────────

    [Fact]
    public void RoleAssignment_Create_SetsProperties()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();

        var assignment = RoleAssignment.Create(userId, roleId, clusterId, "admin@test.com");

        output.WriteLine($"Assignment: user={userId.ToString()[..8]}, role={roleId.ToString()[..8]}, cluster={clusterId.ToString()[..8]}");
        assignment.Id.Should().NotBe(Guid.Empty);
        assignment.UserId.Should().Be(userId);
        assignment.RoleId.Should().Be(roleId);
        assignment.ClusterId.Should().Be(clusterId);
        assignment.AssignedBy.Should().Be("admin@test.com");
        assignment.AssignedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }
}
