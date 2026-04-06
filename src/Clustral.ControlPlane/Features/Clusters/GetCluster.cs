using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Clusters;

// ── Query ────────────────────────────────────────────────────────────────────

public record GetClusterQuery(Guid Id) : IRequest<Result<ClusterResponse>>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetClusterHandler(ClustralDb db)
    : IRequestHandler<GetClusterQuery, Result<ClusterResponse>>
{
    public async Task<Result<ClusterResponse>> Handle(GetClusterQuery request, CancellationToken ct)
    {
        var cluster = await db.Clusters
            .Find(c => c.Id == request.Id)
            .FirstOrDefaultAsync(ct);

        if (cluster is null)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        return ClusterResponse.From(cluster);
    }
}
