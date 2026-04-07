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

        var result = ar.Deny(reviewer.Id, request.Reason);
        if (result.IsFailure) return result.Error!;

        await db.AccessRequests.ReplaceOneAsync(r => r.Id == ar.Id, ar, cancellationToken: ct);

        logger.LogInformation("Access request {RequestId} denied by {Reviewer}: {Reason}",
            request.RequestId, reviewer.Email, request.Reason);

        return await enricher.EnrichAsync(ar, ct);
    }
}
