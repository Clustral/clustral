using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters;

public record DeleteClusterCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteClusterHandler(
    IClusterRepository clusters,
    IAccessTokenRepository tokens,
    IMediator mediator,
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
        await mediator.Publish(new ClusterDeleted(request.Id), ct);

        logger.LogInformation("Cluster {ClusterId} deregistered", request.Id);

        return Result.Success();
    }
}
