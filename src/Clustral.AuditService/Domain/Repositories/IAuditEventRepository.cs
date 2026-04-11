namespace Clustral.AuditService.Domain.Repositories;

/// <summary>
/// Repository interface for audit event persistence. Thin wrapper over
/// MongoDB — implementations live in <c>Infrastructure/</c>.
/// </summary>
public interface IAuditEventRepository
{
    Task InsertAsync(AuditEvent auditEvent, CancellationToken ct = default);
    Task<AuditEvent?> GetByUidAsync(Guid uid, CancellationToken ct = default);
    Task<(List<AuditEvent> Events, long TotalCount)> ListAsync(
        AuditEventFilter filter, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>
/// Filter criteria for querying audit events.
/// All fields are optional — null means "no filter".
/// </summary>
public sealed record AuditEventFilter(
    string? Category = null,
    string? Code = null,
    Severity? Severity = null,
    string? User = null,
    Guid? ClusterId = null,
    Guid? ResourceId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
