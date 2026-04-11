using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.AuditService.Infrastructure;

public sealed class MongoAuditEventRepository(AuditDbContext db) : IAuditEventRepository
{
    public async Task InsertAsync(AuditEvent auditEvent, CancellationToken ct) =>
        await db.AuditEvents.InsertOneAsync(auditEvent, cancellationToken: ct);

    public async Task<AuditEvent?> GetByIdAsync(Guid uid, CancellationToken ct) =>
        await db.AuditEvents.Find(e => e.Uid == uid).FirstOrDefaultAsync(ct);

    public async Task<(List<AuditEvent> Events, long TotalCount)> ListAsync(
        AuditEventFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var fb = Builders<AuditEvent>.Filter;
        var filters = new List<FilterDefinition<AuditEvent>>();

        if (filter.Category is not null)  filters.Add(fb.Eq(e => e.Category, filter.Category));
        if (filter.Code is not null)      filters.Add(fb.Eq(e => e.Code, filter.Code));
        if (filter.Severity.HasValue)     filters.Add(fb.Eq(e => e.Severity, filter.Severity.Value));
        if (filter.User is not null)      filters.Add(fb.Eq(e => e.User, filter.User));
        if (filter.ClusterId.HasValue)    filters.Add(fb.Eq(e => e.ClusterId, filter.ClusterId.Value));
        if (filter.ResourceId.HasValue)   filters.Add(fb.Eq(e => e.ResourceId, filter.ResourceId.Value));
        if (filter.From.HasValue)         filters.Add(fb.Gte(e => e.Time, filter.From.Value));
        if (filter.To.HasValue)           filters.Add(fb.Lte(e => e.Time, filter.To.Value));

        var combined = filters.Count > 0 ? fb.And(filters) : fb.Empty;

        var totalCount = await db.AuditEvents.CountDocumentsAsync(combined, cancellationToken: ct);
        var events = await db.AuditEvents
            .Find(combined)
            .SortByDescending(e => e.Time)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return (events, totalCount);
    }
}
