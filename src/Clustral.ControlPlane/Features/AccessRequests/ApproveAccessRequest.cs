using System.Xml;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record ApproveAccessRequestCommand(Guid RequestId, string? DurationOverride)
    : IRequest<Result<AccessRequestResponse>>;

public sealed class ApproveAccessRequestHandler(
    IAccessRequestRepository accessRequests,
    ICurrentUserProvider currentUser,
    IMediator mediator,
    AccessRequestEnricher enricher,
    ILogger<ApproveAccessRequestHandler> logger)
    : IRequestHandler<ApproveAccessRequestCommand, Result<AccessRequestResponse>>
{
    public async Task<Result<AccessRequestResponse>> Handle(
        ApproveAccessRequestCommand request, CancellationToken ct)
    {
        var reviewer = await currentUser.GetCurrentUserAsync(ct);
        if (reviewer is null) return ResultErrors.UserUnauthorized();

        var ar = await accessRequests.GetByIdAsync(request.RequestId, ct);
        if (ar is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.");

        var grantDuration = ar.RequestedDuration;
        if (!string.IsNullOrEmpty(request.DurationOverride))
        {
            try { grantDuration = XmlConvert.ToTimeSpan(request.DurationOverride); }
            catch { return ResultErrors.InvalidDuration(request.DurationOverride); }
        }

        var result = ar.Approve(reviewer.Id, grantDuration);
        if (result.IsFailure) return result.Error!;

        await accessRequests.ReplaceAsync(ar, ct);
        await mediator.DispatchDomainEventsAsync(ar, ct);

        logger.LogInformation("Access request {RequestId} approved by {Reviewer}",
            request.RequestId, reviewer.Email);

        return await enricher.EnrichAsync(ar, ct);
    }
}
