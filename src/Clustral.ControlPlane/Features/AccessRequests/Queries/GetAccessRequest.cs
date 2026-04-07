using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.AccessRequests.Queries;

public record GetAccessRequestQuery(Guid Id) : IQuery<Result<AccessRequestResponse>>;

public sealed class GetAccessRequestHandler(
    IAccessRequestRepository accessRequests,
    AccessRequestEnricher enricher)
    : IRequestHandler<GetAccessRequestQuery, Result<AccessRequestResponse>>
{
    public async Task<Result<AccessRequestResponse>> Handle(GetAccessRequestQuery request, CancellationToken ct)
    {
        var ar = await accessRequests.GetByIdAsync(request.Id, ct);
        if (ar is null)
            return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.");

        return await enricher.EnrichAsync(ar, ct);
    }
}
