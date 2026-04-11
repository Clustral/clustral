using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.AuditService.Domain;

/// <summary>
/// Persistent audit log entry stored in MongoDB. Follows Teleport's
/// enterprise audit event conventions: structured event codes with
/// severity suffixes, category grouping, and actor/resource fields.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Unique event ID (like Teleport's "uid").</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Uid { get; set; }

    // ── Event identity ───────────────────────────────────────────────────

    /// <summary>
    /// Dotted event type descriptor (e.g. <c>access_request.approved</c>,
    /// <c>credential.issued</c>, <c>proxy.request</c>).
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Unique event code with format <c>[PREFIX][NUMBER][SEVERITY]</c>
    /// (e.g. <c>CAR002I</c>). Prefix identifies the category, number is
    /// sequential, suffix is <c>I</c> (Info), <c>W</c> (Warning), or
    /// <c>E</c> (Error).
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Category for grouping and filtering (e.g. <c>access_requests</c>,
    /// <c>credentials</c>, <c>clusters</c>, <c>proxy</c>, <c>auth</c>).
    /// </summary>
    public string Category { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Severity Severity { get; set; } = Severity.Info;

    public bool Success { get; set; } = true;

    // ── Who ──────────────────────────────────────────────────────────────

    /// <summary>Email or subject of the actor who performed the action.</summary>
    public string? User { get; set; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? UserId { get; set; }

    /// <summary>Source identifier (e.g. <c>clustral-cli/v1.0</c>, <c>web-ui</c>).</summary>
    [BsonIgnoreIfNull]
    public string? UserAgent { get; set; }

    // ── What ─────────────────────────────────────────────────────────────

    /// <summary>Type of the affected resource (e.g. <c>AccessRequest</c>, <c>Cluster</c>).</summary>
    [BsonIgnoreIfNull]
    public string? ResourceType { get; set; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? ResourceId { get; set; }

    /// <summary>Human-readable name of the resource (cluster name, role name).</summary>
    [BsonIgnoreIfNull]
    public string? ResourceName { get; set; }

    // ── Where ────────────────────────────────────────────────────────────

    [BsonIgnoreIfNull]
    public string? ClusterName { get; set; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public Guid? ClusterId { get; set; }

    [BsonIgnoreIfNull]
    public string? ClientIp { get; set; }

    // ── When ─────────────────────────────────────────────────────────────

    /// <summary>When the event occurred at the source service.</summary>
    public DateTimeOffset Time { get; set; }

    /// <summary>When the audit service received and persisted the event.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    // ── Details ──────────────────────────────────────────────────────────

    /// <summary>Human-readable description of what happened.</summary>
    [BsonIgnoreIfNull]
    public string? Message { get; set; }

    /// <summary>Error detail (populated when <see cref="Success"/> is <c>false</c>).</summary>
    [BsonIgnoreIfNull]
    public string? Error { get; set; }

    /// <summary>Full event-specific payload stored as BSON for queryability.</summary>
    [BsonIgnoreIfNull]
    public BsonDocument? Metadata { get; set; }
}

public enum Severity
{
    Info,
    Warning,
    Error,
}
