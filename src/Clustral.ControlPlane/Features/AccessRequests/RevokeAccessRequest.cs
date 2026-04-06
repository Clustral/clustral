using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record RevokeAccessRequestCommand(Guid RequestId, string? Reason)
    : IRequest<Result<AccessRequestResponse>>;

public sealed class RevokeAccessRequestHandler(
    ClustralDb db,
    ICurrentUserProvider currentUser,
    AccessRequestEnricher enricher,
    ILogger<RevokeAccessRequestHandler> logger)
    : IRequestHandler<RevokeAccessRequestCommand, Result<AccessRequestResponse>>
{
    public async Task<Result<AccessRequestResponse>> Handle(
        RevokeAccessRequestCommand request, CancellationToken ct)
    {
        var revoker = await currentUser.GetCurrentUserAsync(ct);
        if (revoker is null) return ResultErrors.UserUnauthorized();

        var ar = await db.AccessRequests.Find(r => r.Id == request.RequestId).FirstOrDefaultAsync(ct);
        if (ar is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.");

        if (ar.Status != AccessRequestStatus.Approved)
            return ResultErrors.GrantNotApproved(ar.Status.ToString());
        if (ar.IsRevoked)
            return ResultErrors.GrantAlreadyRevoked();
        if (!ar.IsGrantActive)
            return ResultErrors.GrantAlreadyExpired();

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessRequest>.Update
            .Set(r => r.Status, AccessRequestStatus.Revoked)
            .Set(r => r.RevokedAt, now)
            .Set(r => r.RevokedBy, revoker.Id)
            .Set(r => r.RevokedReason, request.Reason);

        await db.AccessRequests.UpdateOneAsync(r => r.Id == request.RequestId, update, cancellationToken: ct);

        logger.LogInformation("Access request {RequestId} revoked by {Revoker}",
            request.RequestId, revoker.Email);

        var updated = await db.AccessRequests.Find(r => r.Id == request.RequestId).FirstOrDefaultAsync(ct);
        return await enricher.EnrichAsync(updated!, ct);
    }
}
