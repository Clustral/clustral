using System.Xml;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record ApproveAccessRequestCommand(Guid RequestId, string? DurationOverride)
    : IRequest<Result<AccessRequestResponse>>;

public sealed class ApproveAccessRequestHandler(
    ClustralDb db,
    ICurrentUserProvider currentUser,
    AccessRequestEnricher enricher,
    ILogger<ApproveAccessRequestHandler> logger)
    : IRequestHandler<ApproveAccessRequestCommand, Result<AccessRequestResponse>>
{
    public async Task<Result<AccessRequestResponse>> Handle(
        ApproveAccessRequestCommand request, CancellationToken ct)
    {
        var reviewer = await currentUser.GetCurrentUserAsync(ct);
        if (reviewer is null) return ResultErrors.UserUnauthorized();

        var ar = await db.AccessRequests.Find(r => r.Id == request.RequestId).FirstOrDefaultAsync(ct);
        if (ar is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.");

        if (ar.Status != AccessRequestStatus.Pending)
            return ResultErrors.RequestNotPending(ar.Status.ToString());

        if (ar.IsPendingExpired)
            return ResultErrors.RequestExpired();

        var now = DateTimeOffset.UtcNow;
        var grantDuration = ar.RequestedDuration;

        if (!string.IsNullOrEmpty(request.DurationOverride))
        {
            try { grantDuration = XmlConvert.ToTimeSpan(request.DurationOverride); }
            catch { return ResultErrors.InvalidDuration(request.DurationOverride); }
        }

        var update = Builders<AccessRequest>.Update
            .Set(r => r.Status, AccessRequestStatus.Approved)
            .Set(r => r.ReviewerId, reviewer.Id)
            .Set(r => r.ReviewedAt, now)
            .Set(r => r.GrantExpiresAt, now + grantDuration);

        await db.AccessRequests.UpdateOneAsync(r => r.Id == request.RequestId, update, cancellationToken: ct);

        logger.LogInformation("Access request {RequestId} approved by {Reviewer}",
            request.RequestId, reviewer.Email);

        var updated = await db.AccessRequests.Find(r => r.Id == request.RequestId).FirstOrDefaultAsync(ct);
        return await enricher.EnrichAsync(updated!, ct);
    }
}
