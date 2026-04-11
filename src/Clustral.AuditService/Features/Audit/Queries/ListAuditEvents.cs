using Clustral.AuditService.Api.Controllers;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using Clustral.Sdk.Cqs;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.AuditService.Features.Audit.Queries;

public sealed record ListAuditEventsQuery(
    string? Category,
    string? Code,
    string? SeverityFilter,
    string? User,
    Guid? ClusterId,
    Guid? ResourceId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize) : IQuery<Result<AuditListResponse>>;

public sealed class ListAuditEventsHandler(AuditDbContext db)
    : IRequestHandler<ListAuditEventsQuery, Result<AuditListResponse>>
{
    public async Task<Result<AuditListResponse>> Handle(
        ListAuditEventsQuery request, CancellationToken ct)
    {
        var filterBuilder = Builders<AuditEvent>.Filter;
        var filters = new List<FilterDefinition<AuditEvent>>();

        if (!string.IsNullOrEmpty(request.Category))
            filters.Add(filterBuilder.Eq(e => e.Category, request.Category));
        if (!string.IsNullOrEmpty(request.Code))
            filters.Add(filterBuilder.Eq(e => e.Code, request.Code));
        if (!string.IsNullOrEmpty(request.SeverityFilter) &&
            Enum.TryParse<Severity>(request.SeverityFilter, ignoreCase: true, out var sev))
            filters.Add(filterBuilder.Eq(e => e.Severity, sev));
        if (!string.IsNullOrEmpty(request.User))
            filters.Add(filterBuilder.Eq(e => e.User, request.User));
        if (request.ClusterId.HasValue)
            filters.Add(filterBuilder.Eq(e => e.ClusterId, request.ClusterId.Value));
        if (request.ResourceId.HasValue)
            filters.Add(filterBuilder.Eq(e => e.ResourceId, request.ResourceId.Value));
        if (request.From.HasValue)
            filters.Add(filterBuilder.Gte(e => e.Time, request.From.Value));
        if (request.To.HasValue)
            filters.Add(filterBuilder.Lte(e => e.Time, request.To.Value));

        var filter = filters.Count > 0 ? filterBuilder.And(filters) : filterBuilder.Empty;
        var totalCount = await db.AuditEvents.CountDocumentsAsync(filter, cancellationToken: ct);

        var events = await db.AuditEvents
            .Find(filter)
            .SortByDescending(e => e.Time)
            .Skip((request.Page - 1) * request.PageSize)
            .Limit(request.PageSize)
            .ToListAsync(ct);

        return new AuditListResponse
        {
            Events = events.Select(MapToResponse).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize),
        };
    }

    internal static AuditEventResponse MapToResponse(AuditEvent e) => new()
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
    };
}
