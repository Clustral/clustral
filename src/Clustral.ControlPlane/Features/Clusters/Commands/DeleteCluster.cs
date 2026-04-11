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
    ICurrentUserProvider currentUser,
    IMediator mediator,
    ILogger<DeleteClusterHandler> logger)
    : IRequestHandler<DeleteClusterCommand, Result>
{
    public async Task<Result> Handle(DeleteClusterCommand request, CancellationToken ct)
    {
        var cluster = await clusters.GetByIdAsync(request.Id, ct);
        if (cluster is null)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        var deleted = await clusters.DeleteAsync(request.Id, ct);
        if (!deleted)
            return ResultErrors.ClusterNotFound(request.Id.ToString());

        await tokens.DeleteByClusterIdAsync(request.Id, ct);
        await mediator.Publish(new ClusterDeleted(request.Id, cluster.Name, currentUser.Email), ct);

        logger.LogInformation("Cluster {ClusterId} ({Name}) deleted by {Email}",
            request.Id, cluster.Name, currentUser.Email);

        return Result.Success();
    }
}
