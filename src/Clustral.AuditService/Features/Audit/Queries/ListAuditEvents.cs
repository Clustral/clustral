using Clustral.AuditService.Api.Controllers;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Domain.Repositories;
using Clustral.Sdk.Cqs;
using Clustral.Sdk.Results;
using MediatR;

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

public sealed class ListAuditEventsHandler(IAuditEventRepository repository)
    : IRequestHandler<ListAuditEventsQuery, Result<AuditListResponse>>
{
    public async Task<Result<AuditListResponse>> Handle(
        ListAuditEventsQuery request, CancellationToken ct)
    {
        Severity? severity = null;
        if (!string.IsNullOrEmpty(request.SeverityFilter) &&
            Enum.TryParse<Severity>(request.SeverityFilter, ignoreCase: true, out var sev))
            severity = sev;

        var filter = new AuditEventFilter(
            request.Category, request.Code, severity, request.User,
            request.ClusterId, request.ResourceId, request.From, request.To);

        var (events, totalCount) = await repository.ListAsync(
            filter, request.Page, request.PageSize, ct);

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
        ReceivedAt = e.ReceivedAt,
        Message = e.Message,
        Error = e.Error,
        Metadata = e.Metadata?.ToDictionary(
            el => el.Name,
            el => MongoDB.Bson.BsonTypeMapper.MapToDotNetValue(el.Value) as object),
    };
}
