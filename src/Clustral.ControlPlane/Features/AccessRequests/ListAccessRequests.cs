using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Domain.Specifications;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record ListAccessRequestsQuery(string? Status, bool Mine, bool Active)
    : IRequest<Result<AccessRequestListResponse>>;

public sealed class ListAccessRequestsHandler(
    IAccessRequestRepository accessRequests,
    ICurrentUserProvider currentUser,
    AccessRequestEnricher enricher)
    : IRequestHandler<ListAccessRequestsQuery, Result<AccessRequestListResponse>>
{
    public async Task<Result<AccessRequestListResponse>> Handle(
        ListAccessRequestsQuery request, CancellationToken ct)
    {
        var builder = Builders<AccessRequest>.Filter;
        var filter = builder.Empty;

        if (request.Active)
        {
            filter &= AccessSpecifications.ActiveGrantFilter();
        }
        else if (!string.IsNullOrEmpty(request.Status) &&
                 Enum.TryParse<AccessRequestStatus>(request.Status, true, out var s))
        {
            filter &= builder.Eq(r => r.Status, s);
        }

        if (request.Mine)
        {
            var user = await currentUser.GetCurrentUserAsync(ct);
            if (user is null) return ResultErrors.UserUnauthorized();
            filter &= builder.Eq(r => r.RequesterId, user.Id);
        }

        var requests = await accessRequests.FindAsync(filter, 100, ct);

        var enriched = new List<AccessRequestResponse>();
        foreach (var r in requests)
            enriched.Add(await enricher.EnrichAsync(r, ct));

        return new AccessRequestListResponse(enriched);
    }
}
