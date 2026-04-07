using Clustral.ControlPlane.Domain.Repositories;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters;

// ── Command ──────────────────────────────────────────────────────────────────

public record DeleteClusterCommand(Guid Id) : IRequest<Result>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class DeleteClusterHandler(
    IClusterRepository clusters,
    IAccessTokenRepository tokens,
    ILogger<DeleteClusterHandler> logger)
    : IRequestHandler<DeleteClusterCommand, Result>
{
    public async Task<Result> Handle(DeleteClusterCommand request, CancellationToken ct)
    {
        var deleted = await clusters.DeleteAsync(request.Id, ct);
        if (!deleted)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        // Cascade: delete all access tokens for this cluster.
        await tokens.DeleteByClusterIdAsync(request.Id, ct);
        logger.LogInformation("Cluster {ClusterId} deregistered", request.Id);

        return Result.Success();
    }
}
