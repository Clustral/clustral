using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters.Commands;

public record DeleteClusterCommand(Guid Id) : ICommand;

public sealed class DeleteClusterHandler(
    IClusterRepository clusters,
    IAccessTokenRepository tokens,
    IMediator mediator,
    ILogger<DeleteClusterHandler> logger)
    : IRequestHandler<DeleteClusterCommand, Result>
{
    public async Task<Result> Handle(DeleteClusterCommand request, CancellationToken ct)
    {
        // Read before delete so the domain event carries the cluster name.
        var cluster = await clusters.GetByIdAsync(request.Id, ct);
        if (cluster is null)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        var deleted = await clusters.DeleteAsync(request.Id, ct);
        if (!deleted)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        // Cascade: delete all access tokens for this cluster.
        await tokens.DeleteByClusterIdAsync(request.Id, ct);
        await mediator.Publish(new ClusterDeleted(request.Id, cluster.Name), ct);

        logger.LogInformation("Cluster {ClusterId} ({Name}) deregistered",
            request.Id, cluster.Name);

        return Result.Success();
    }
}
