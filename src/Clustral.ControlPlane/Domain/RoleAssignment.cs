using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain;

/// <summary>
/// Assigns a <see cref="Role"/> to a <see cref="User"/> for a specific
/// <see cref="Cluster"/>. One role per user per cluster.
/// </summary>
public sealed class RoleAssignment
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid RoleId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid ClusterId { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    public string AssignedBy { get; set; } = string.Empty;
}
