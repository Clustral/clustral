using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public class RoleTests(ITestOutputHelper output)
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var role = new Role();

        output.WriteLine($"Name:             \"{role.Name}\"");
        output.WriteLine($"Description:      \"{role.Description}\"");
        output.WriteLine($"KubernetesGroups: {role.KubernetesGroups.Count} items");

        Assert.Equal(string.Empty, role.Name);
        Assert.Equal(string.Empty, role.Description);
        Assert.Empty(role.KubernetesGroups);
    }

    [Fact]
    public void KubernetesGroups_AdminRole()
    {
        var role = new Role
        {
            Name = "admin",
            Description = "Full cluster access",
            KubernetesGroups = ["system:masters"],
        };

        output.WriteLine($"Role: {role.Name}");
        output.WriteLine($"Groups: [{string.Join(", ", role.KubernetesGroups)}]");

        Assert.Single(role.KubernetesGroups);
        Assert.Contains("system:masters", role.KubernetesGroups);
    }

    [Fact]
    public void KubernetesGroups_MultipleGroups()
    {
        var role = new Role
        {
            Name = "developer",
            KubernetesGroups = ["clustral-dev", "system:authenticated", "dev-team"],
        };

        output.WriteLine($"Role: {role.Name}");
        output.WriteLine($"Groups: [{string.Join(", ", role.KubernetesGroups)}]");

        Assert.Equal(3, role.KubernetesGroups.Count);
    }
}
