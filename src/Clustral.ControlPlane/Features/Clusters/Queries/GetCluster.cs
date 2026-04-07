using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

public record GetClusterQuery(Guid Id) : IQuery<Result<ClusterResponse>>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetClusterHandler(IClusterRepository clusters)
    : IRequestHandler<GetClusterQuery, Result<ClusterResponse>>
{
    public async Task<Result<ClusterResponse>> Handle(GetClusterQuery request, CancellationToken ct)
    {
        var cluster = await clusters.GetByIdAsync(request.Id, ct);

        if (cluster is null)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        return ClusterResponse.From(cluster);
    }
}
