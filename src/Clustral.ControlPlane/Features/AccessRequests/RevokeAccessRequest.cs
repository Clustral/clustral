using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record RevokeAccessRequestCommand(Guid RequestId, string? Reason)
    : IRequest<Result<AccessRequestResponse>>;

public sealed class RevokeAccessRequestHandler(
    IAccessRequestRepository accessRequests,
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

        var ar = await accessRequests.GetByIdAsync(request.RequestId, ct);
        if (ar is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.");

        var result = ar.Revoke(revoker.Id, request.Reason);
        if (result.IsFailure) return result.Error!;

        await accessRequests.ReplaceAsync(ar, ct);

        logger.LogInformation("Access request {RequestId} revoked by {Revoker}",
            request.RequestId, revoker.Email);

        return await enricher.EnrichAsync(ar, ct);
    }
}
