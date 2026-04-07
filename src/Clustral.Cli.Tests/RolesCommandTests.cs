using System.Text.Json;
using Clustral.Cli.Commands;
using Clustral.Cli.Config;
using Spectre.Console.Testing;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class RolesCommandTests(ITestOutputHelper output)
{
    // ── Command tree ────────────────────────────────────────────────────────

    [Fact]
    public void RolesCommand_HasLsSubcommand()
    {
        var roles = RolesCommand.BuildRolesCommand();

        output.WriteLine($"Command: {roles.Name}");
        foreach (var sub in roles.Subcommands)
            output.WriteLine($"  {sub.Name} — {sub.Description}");

        Assert.Equal("roles", roles.Name);
        Assert.Contains(roles.Subcommands, s => s.Name == "list");
    }

    [Fact]
    public void RolesLs_HasInsecureOption()
    {
        var roles = RolesCommand.BuildRolesCommand();
        var ls = roles.Subcommands.First(s => s.Name == "list");

        Assert.Contains(ls.Options, o => o.Name == "insecure");
    }

    // ── Table rendering ─────────────────────────────────────────────────────

    [Fact]
    public void RenderRolesTable_MultipleRoles()
    {
        var roles = new List<RoleResponse>
        {
            new() { Name = "admin", Description = "Full cluster access", KubernetesGroups = ["system:masters"] },
            new() { Name = "developer", Description = "Dev namespace access", KubernetesGroups = ["clustral-dev", "system:authenticated"] },
            new() { Name = "read-only", Description = "View-only access", KubernetesGroups = ["clustral-viewer"] },
        };

        var console = new TestConsole();
        console.Profile.Width = 100;
        RolesCommand.RenderRolesTable(console, roles);

        output.WriteLine("=== Roles Table ===");
        output.WriteLine(console.Output);

        Assert.Contains("admin", console.Output);
        Assert.Contains("developer", console.Output);
        Assert.Contains("read-only", console.Output);
        Assert.Contains("system:masters", console.Output);
        Assert.Contains("clustral-dev", console.Output);
    }

    [Fact]
    public void RenderRolesTable_NoGroups()
    {
        var roles = new List<RoleResponse>
        {
            new() { Name = "empty-role", Description = "No groups", KubernetesGroups = [] },
        };

        var console = new TestConsole();
        console.Profile.Width = 100;
        RolesCommand.RenderRolesTable(console, roles);

        output.WriteLine("=== Role With No Groups ===");
        output.WriteLine(console.Output);

        Assert.Contains("empty-role", console.Output);
    }

    [Fact]
    public void RenderRolesTable_SingleRole()
    {
        var roles = new List<RoleResponse>
        {
            new()
            {
                Name = "cluster-admin",
                Description = "Full administrative access to all clusters",
                KubernetesGroups = ["system:masters", "system:authenticated"],
            },
        };

        var console = new TestConsole();
        console.Profile.Width = 120;
        RolesCommand.RenderRolesTable(console, roles);

        output.WriteLine("=== Single Role ===");
        output.WriteLine(console.Output);

        Assert.Contains("cluster-admin", console.Output);
        Assert.Contains("system:masters", console.Output);
    }

    // ── Wire type ───────────────────────────────────────────────────────────

    [Fact]
    public void RoleListResponse_Deserializes()
    {
        var json = """
        {
            "roles": [
                {
                    "id": "r1",
                    "name": "admin",
                    "description": "Full access",
                    "kubernetesGroups": ["system:masters"],
                    "createdAt": "2026-01-01T00:00:00Z"
                },
                {
                    "id": "r2",
                    "name": "viewer",
                    "description": "Read-only",
                    "kubernetesGroups": ["clustral-viewer"],
                    "createdAt": "2026-02-01T00:00:00Z"
                }
            ]
        }
        """;

        var resp = JsonSerializer.Deserialize(json, CliJsonContext.Default.RoleListResponse);

        output.WriteLine($"Roles: {resp!.Roles.Count}");
        foreach (var r in resp.Roles)
            output.WriteLine($"  {r.Name} — [{string.Join(", ", r.KubernetesGroups)}]");

        Assert.Equal(2, resp.Roles.Count);
        Assert.Equal("admin", resp.Roles[0].Name);
        Assert.Contains("system:masters", resp.Roles[0].KubernetesGroups);
    }

    [Fact]
    public void RoleResponse_AllFields()
    {
        var json = """
        {
            "id": "role-123",
            "name": "developer",
            "description": "Dev access",
            "kubernetesGroups": ["dev-team", "system:authenticated"],
            "createdAt": "2026-03-15T10:00:00Z"
        }
        """;

        var role = JsonSerializer.Deserialize<RoleResponse>(json, CliJsonContext.Default.Options);

        output.WriteLine($"Role: {role!.Name}");
        output.WriteLine($"Description: {role.Description}");
        output.WriteLine($"Groups: [{string.Join(", ", role.KubernetesGroups)}]");
        output.WriteLine($"CreatedAt: {role.CreatedAt}");

        Assert.Equal("developer", role.Name);
        Assert.Equal(2, role.KubernetesGroups.Count);
    }
}
