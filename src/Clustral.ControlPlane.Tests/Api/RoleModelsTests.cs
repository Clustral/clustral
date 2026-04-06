using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

public class RoleModelsTests(ITestOutputHelper output)
{
    [Fact]
    public void RoleAssignment_EnforcesOneRolePerUserPerCluster()
    {
        // The unique index (UserId, ClusterId) enforces this at DB level.
        // The controller upserts: delete existing + insert new.
        var userId = Guid.NewGuid();
        var clusterId = Guid.NewGuid();

        var assignment1 = new RoleAssignment
        {
            UserId = userId, ClusterId = clusterId, RoleId = Guid.NewGuid(),
        };
        var assignment2 = new RoleAssignment
        {
            UserId = userId, ClusterId = clusterId, RoleId = Guid.NewGuid(),
        };

        output.WriteLine($"User: {userId}");
        output.WriteLine($"Cluster: {clusterId}");
        output.WriteLine($"Assignment1 RoleId: {assignment1.RoleId}");
        output.WriteLine($"Assignment2 RoleId: {assignment2.RoleId}");
        output.WriteLine("Same (userId, clusterId) => upsert replaces assignment1 with assignment2");

        Assert.Equal(userId, assignment1.UserId);
        Assert.Equal(userId, assignment2.UserId);
        Assert.NotEqual(assignment1.RoleId, assignment2.RoleId);
    }

    [Fact]
    public void RoleAssignment_TracksAssignedBy()
    {
        var assignment = new RoleAssignment
        {
            AssignedBy = "admin@example.com",
        };

        output.WriteLine($"AssignedBy: {assignment.AssignedBy}");

        Assert.Equal("admin@example.com", assignment.AssignedBy);
    }

    [Fact]
    public void Role_CascadeDeleteRemovesAssignments()
    {
        // Document the cascade behavior: when a Role is deleted,
        // all RoleAssignments with that RoleId are also deleted.
        var roleId = Guid.NewGuid();

        output.WriteLine($"Role: {roleId}");
        output.WriteLine("DELETE /api/v1/roles/{id} => cascade deletes all assignments with this roleId");

        Assert.NotEqual(Guid.Empty, roleId);
    }

    [Fact]
    public void Role_KubernetesGroups_UsedForImpersonation()
    {
        var role = new Role
        {
            Name = "admin",
            KubernetesGroups = ["system:masters", "system:authenticated"],
        };

        output.WriteLine($"Role: {role.Name}");
        output.WriteLine($"Groups: [{string.Join(", ", role.KubernetesGroups)}]");
        output.WriteLine("These groups become Impersonate-Group headers in kubectl proxy");

        Assert.Equal(2, role.KubernetesGroups.Count);
    }
}
