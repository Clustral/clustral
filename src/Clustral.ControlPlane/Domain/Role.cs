using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// A Clustral role that maps to a set of Kubernetes groups for impersonation.
/// Admins assign roles to users per cluster.
/// </summary>
public sealed class Role
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
}
