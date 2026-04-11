using Clustral.AuditService.Api.Controllers;
using Clustral.AuditService.Infrastructure;
using Clustral.Sdk.Cqs;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.AuditService.Features.Audit.Queries;

public sealed record GetAuditEventQuery(Guid Uid) : IQuery<Result<AuditEventResponse>>;

public sealed class GetAuditEventHandler(AuditDbContext db)
    : IRequestHandler<GetAuditEventQuery, Result<AuditEventResponse>>
{
    public async Task<Result<AuditEventResponse>> Handle(
        GetAuditEventQuery request, CancellationToken ct)
    {
        var auditEvent = await db.AuditEvents
            .Find(e => e.Uid == request.Uid)
            .FirstOrDefaultAsync(ct);

        if (auditEvent is null)
            return ResultError.NotFound("AUDIT_EVENT_NOT_FOUND", $"Audit event '{request.Uid}' not found.");

        return ListAuditEventsHandler.MapToResponse(auditEvent);
    }
}
