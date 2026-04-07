using Clustral.ControlPlane.Domain.Events;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// A Clustral role that maps to a set of Kubernetes groups for impersonation.
/// Admins assign roles to users per cluster.
/// </summary>
public sealed class Role : HasDomainEvents
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Kubernetes groups to impersonate when a user with this role
    /// accesses a cluster. Example: <c>["system:masters"]</c> for admin,
    /// <c>["clustral-viewer"]</c> for read-only.
    /// </summary>
    public List<string> KubernetesGroups { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ── Aggregate methods ────────────────────────────────────────────────

    /// <summary>
    /// Creates a new role with the given Kubernetes groups.
    /// </summary>
    public static Role Create(string name, string description, List<string>? kubernetesGroups = null)
    {
        var groups = kubernetesGroups ?? [];
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            KubernetesGroups = groups,
        };
        role.RaiseDomainEvent(new RoleCreated(role.Id, name, groups));
        return role;
    }

    /// <summary>
    /// Applies partial updates to the role. Null values are skipped.
    /// </summary>
    public void Update(string? name, string? description, List<string>? kubernetesGroups)
    {
        if (name is not null) Name = name;
        if (description is not null) Description = description;
        if (kubernetesGroups is not null) KubernetesGroups = kubernetesGroups;
        RaiseDomainEvent(new RoleUpdated(Id, name, description, kubernetesGroups));
    }
}
