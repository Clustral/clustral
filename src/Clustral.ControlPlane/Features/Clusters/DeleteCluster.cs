using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Clusters;

// ── Command ──────────────────────────────────────────────────────────────────

public record DeleteClusterCommand(Guid Id) : IRequest<Result>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class DeleteClusterHandler(ClustralDb db, ILogger<DeleteClusterHandler> logger)
    : IRequestHandler<DeleteClusterCommand, Result>
{
    public async Task<Result> Handle(DeleteClusterCommand request, CancellationToken ct)
    {
        var deleteResult = await db.Clusters.DeleteOneAsync(c => c.Id == request.Id, ct);
        if (deleteResult.DeletedCount == 0)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        // Cascade: delete all access tokens for this cluster.
        await db.AccessTokens.DeleteManyAsync(t => t.ClusterId == request.Id, ct);
        logger.LogInformation("Cluster {ClusterId} deregistered", request.Id);

        return Result.Success();
    }
}
