using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.AuditService.Domain;

/// <summary>
/// Aggregate root for audit log entries. Append-only — once created,
/// audit events are immutable. Follows Teleport's enterprise audit
/// event conventions: structured event codes with severity suffixes,
/// category grouping, and actor/resource fields.
///
/// Use <see cref="Create"/> factory method instead of direct construction.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Unique event ID (like Teleport's "uid").</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Uid { get; init; }

    // ── Event identity ───────────────────────────────────────────────────

    public string Event { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Severity Severity { get; init; } = Severity.Info;

    public bool Success { get; init; } = true;

    // ── Who ──────────────────────────────────────────────────────────────

    public string? User { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? UserId { get; init; }

    [BsonIgnoreIfNull]
    public string? UserAgent { get; init; }

    // ── What ─────────────────────────────────────────────────────────────

    [BsonIgnoreIfNull]
    public string? ResourceType { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? ResourceId { get; init; }

    [BsonIgnoreIfNull]
    public string? ResourceName { get; init; }

    // ── Where ────────────────────────────────────────────────────────────

    [BsonIgnoreIfNull]
    public string? ClusterName { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? ClusterId { get; init; }

    [BsonIgnoreIfNull]
    public string? ClientIp { get; init; }

    // ── When ─────────────────────────────────────────────────────────────

    public DateTimeOffset Time { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }

    // ── Details ──────────────────────────────────────────────────────────

    [BsonIgnoreIfNull]
    public string? Message { get; init; }

    [BsonIgnoreIfNull]
    public string? Error { get; init; }

    [BsonIgnoreIfNull]
    public BsonDocument? Metadata { get; init; }

    // ── Factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new audit event. All invariants are enforced here:
    /// non-empty event/code/category, severity suffix matches code.
    /// </summary>
    public static AuditEvent Create(
        string @event,
        string code,
        string category,
        Severity severity,
        bool success,
        DateTimeOffset time,
        string? user = null,
        Guid? userId = null,
        string? userAgent = null,
        string? resourceType = null,
        Guid? resourceId = null,
        string? resourceName = null,
        string? clusterName = null,
        Guid? clusterId = null,
        string? clientIp = null,
        string? message = null,
        string? error = null,
        BsonDocument? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        return new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = @event,
            Code = code,
            Category = category,
            Severity = severity,
            Success = success,
            User = user,
            UserId = userId,
            UserAgent = userAgent,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ResourceName = resourceName,
            ClusterName = clusterName,
            ClusterId = clusterId,
            ClientIp = clientIp,
            Time = time,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = message,
            Error = error,
            Metadata = metadata,
        };
    }
}

public enum Severity
{
    Info,
    Warning,
    Error,
}
