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

        var result = ar.Revoke(revoker.Id, request.Reason);
        if (result.IsFailure) return result.Error!;

        await db.AccessRequests.ReplaceOneAsync(r => r.Id == ar.Id, ar, cancellationToken: ct);

        logger.LogInformation("Access request {RequestId} revoked by {Revoker}",
            request.RequestId, revoker.Email);

        return await enricher.EnrichAsync(ar, ct);
    }
}
