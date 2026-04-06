using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record DenyAccessRequestCommand(Guid RequestId, string Reason)
    : IRequest<Result<AccessRequestResponse>>;

public sealed class DenyAccessRequestHandler(
    ClustralDb db,
    ICurrentUserProvider currentUser,
    AccessRequestEnricher enricher,
    ILogger<DenyAccessRequestHandler> logger)
    : IRequestHandler<DenyAccessRequestCommand, Result<AccessRequestResponse>>
{
    public async Task<Result<AccessRequestResponse>> Handle(
        DenyAccessRequestCommand request, CancellationToken ct)
    {
        var reviewer = await currentUser.GetCurrentUserAsync(ct);
        if (reviewer is null) return ResultErrors.UserUnauthorized();

        var ar = await db.AccessRequests.Find(r => r.Id == request.RequestId).FirstOrDefaultAsync(ct);
        if (ar is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.");

        if (ar.Status != AccessRequestStatus.Pending)
            return ResultErrors.RequestNotPending(ar.Status.ToString());

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessRequest>.Update
            .Set(r => r.Status, AccessRequestStatus.Denied)
            .Set(r => r.ReviewerId, reviewer.Id)
            .Set(r => r.ReviewedAt, now)
            .Set(r => r.DenialReason, request.Reason);

        await db.AccessRequests.UpdateOneAsync(r => r.Id == request.RequestId, update, cancellationToken: ct);

        logger.LogInformation("Access request {RequestId} denied by {Reviewer}: {Reason}",
            request.RequestId, reviewer.Email, request.Reason);

        var updated = await db.AccessRequests.Find(r => r.Id == request.RequestId).FirstOrDefaultAsync(ct);
        return await enricher.EnrichAsync(updated!, ct);
    }
}
