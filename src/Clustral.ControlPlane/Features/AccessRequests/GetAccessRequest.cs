using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record GetAccessRequestQuery(Guid Id) : IRequest<Result<AccessRequestResponse>>;

public sealed class GetAccessRequestHandler(ClustralDb db, AccessRequestEnricher enricher)
    : IRequestHandler<GetAccessRequestQuery, Result<AccessRequestResponse>>
{
    public async Task<Result<AccessRequestResponse>> Handle(GetAccessRequestQuery request, CancellationToken ct)
    {
        var ar = await db.AccessRequests.Find(r => r.Id == request.Id).FirstOrDefaultAsync(ct);
        if (ar is null)
            return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.");

        return await enricher.EnrichAsync(ar, ct);
    }
}
