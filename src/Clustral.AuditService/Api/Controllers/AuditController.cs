using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Clustral.AuditService.Api.Controllers;

/// <summary>
/// REST API for querying audit events. Supports filtering by category,
/// code, severity, user, cluster, resource, and time range with pagination.
/// </summary>
[ApiController]
[Route("api/v1/audit")]
public sealed class AuditController(AuditDbContext db) : ControllerBase
{
    /// <summary>
    /// Lists audit events with optional filtering and pagination.
    /// Results are ordered by time descending (newest first).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<AuditListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? category = null,
        [FromQuery] string? code = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? user = null,
        [FromQuery] Guid? clusterId = null,
        [FromQuery] Guid? resourceId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) return BadRequest("Page must be >= 1.");
        if (pageSize is < 1 or > 200) return BadRequest("PageSize must be between 1 and 200.");

        var filterBuilder = Builders<AuditEvent>.Filter;
        var filters = new List<FilterDefinition<AuditEvent>>();

        if (!string.IsNullOrEmpty(category))
            filters.Add(filterBuilder.Eq(e => e.Category, category));

        if (!string.IsNullOrEmpty(code))
            filters.Add(filterBuilder.Eq(e => e.Code, code));

        if (!string.IsNullOrEmpty(severity) && Enum.TryParse<Severity>(severity, ignoreCase: true, out var sev))
            filters.Add(filterBuilder.Eq(e => e.Severity, sev));

        if (!string.IsNullOrEmpty(user))
            filters.Add(filterBuilder.Eq(e => e.User, user));

        if (clusterId.HasValue)
            filters.Add(filterBuilder.Eq(e => e.ClusterId, clusterId.Value));

        if (resourceId.HasValue)
            filters.Add(filterBuilder.Eq(e => e.ResourceId, resourceId.Value));

        if (from.HasValue)
            filters.Add(filterBuilder.Gte(e => e.Time, from.Value));

        if (to.HasValue)
            filters.Add(filterBuilder.Lte(e => e.Time, to.Value));

        var filter = filters.Count > 0
            ? filterBuilder.And(filters)
            : filterBuilder.Empty;

        var totalCount = await db.AuditEvents.CountDocumentsAsync(filter, cancellationToken: ct);

        var events = await db.AuditEvents
            .Find(filter)
            .SortByDescending(e => e.Time)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return Ok(new AuditListResponse
        {
            Events = events.Select(e => new AuditEventResponse
            {
                Uid = e.Uid,
                Event = e.Event,
                Code = e.Code,
                Category = e.Category,
                Severity = e.Severity.ToString(),
                Success = e.Success,
                User = e.User,
                UserId = e.UserId,
                ResourceType = e.ResourceType,
                ResourceId = e.ResourceId,
                ResourceName = e.ResourceName,
                ClusterName = e.ClusterName,
                ClusterId = e.ClusterId,
                Time = e.Time,
                Message = e.Message,
                Error = e.Error,
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        });
    }

    /// <summary>
    /// Gets a single audit event by its unique ID.
    /// </summary>
    [HttpGet("{uid:guid}")]
    [ProducesResponseType<AuditEventResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid uid, CancellationToken ct)
    {
        var auditEvent = await db.AuditEvents
            .Find(e => e.Uid == uid)
            .FirstOrDefaultAsync(ct);

        if (auditEvent is null)
            return NotFound();

        return Ok(new AuditEventResponse
        {
            Uid = auditEvent.Uid,
            Event = auditEvent.Event,
            Code = auditEvent.Code,
            Category = auditEvent.Category,
            Severity = auditEvent.Severity.ToString(),
            Success = auditEvent.Success,
            User = auditEvent.User,
            UserId = auditEvent.UserId,
            ResourceType = auditEvent.ResourceType,
            ResourceId = auditEvent.ResourceId,
            ResourceName = auditEvent.ResourceName,
            ClusterName = auditEvent.ClusterName,
            ClusterId = auditEvent.ClusterId,
            Time = auditEvent.Time,
            Message = auditEvent.Message,
            Error = auditEvent.Error,
        });
    }
}

// ── Response DTOs ────────────────────────────────────────────────────────────

public sealed class AuditListResponse
{
    public List<AuditEventResponse> Events { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class AuditEventResponse
{
    public Guid Uid { get; init; }
    public string Event { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? User { get; init; }
    public Guid? UserId { get; init; }
    public string? ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public string? ResourceName { get; init; }
    public string? ClusterName { get; init; }
    public Guid? ClusterId { get; init; }
    public DateTimeOffset Time { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
}
