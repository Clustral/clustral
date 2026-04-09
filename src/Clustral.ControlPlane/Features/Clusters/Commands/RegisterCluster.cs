using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters.Commands;

// ── Command ──────────────────────────────────────────────────────────────────

public record RegisterClusterCommand(
    string Name,
    string Description,
    Dictionary<string, string>? Labels) : ICommand<Result<RegisterClusterRestResponse>>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class RegisterClusterHandler(
    IClusterRepository clusters,
    IMediator mediator,
    TokenHashingService tokens,
    ILogger<RegisterClusterHandler> logger)
    : IRequestHandler<RegisterClusterCommand, Result<RegisterClusterRestResponse>>
{
    public async Task<Result<RegisterClusterRestResponse>> Handle(
        RegisterClusterCommand request, CancellationToken ct)
    {
        var exists = await clusters.ExistsByNameAsync(request.Name, ct);
        if (exists)
            return ResultErrors.DuplicateClusterName(request.Name);

        var bootstrapToken = tokens.GenerateToken();
        var tokenHash = tokens.HashToken(bootstrapToken);

        var cluster = Cluster.Create(
            request.Name, request.Description, tokenHash, request.Labels);

        await clusters.InsertAsync(cluster, ct);
        await mediator.DispatchDomainEventsAsync(cluster, ct);
        logger.LogInformation("Cluster {Name} registered with id {Id}", cluster.Name, cluster.Id);

        return new RegisterClusterRestResponse(cluster.Id, bootstrapToken);
    }
}
