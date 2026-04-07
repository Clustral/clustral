using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Clusters;

// ── Command ──────────────────────────────────────────────────────────────────

public record RegisterClusterCommand(
    string Name,
    string Description,
    string AgentPublicKeyPem,
    Dictionary<string, string>? Labels) : IRequest<Result<RegisterClusterRestResponse>>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class RegisterClusterHandler(
    ClustralDb db,
    TokenHashingService tokens,
    ILogger<RegisterClusterHandler> logger)
    : IRequestHandler<RegisterClusterCommand, Result<RegisterClusterRestResponse>>
{
    public async Task<Result<RegisterClusterRestResponse>> Handle(
        RegisterClusterCommand request, CancellationToken ct)
    {
        var exists = await db.Clusters.Find(c => c.Name == request.Name).AnyAsync(ct);
        if (exists)
            return ResultErrors.DuplicateClusterName(request.Name);

        var bootstrapToken = tokens.GenerateToken();
        var tokenHash = tokens.HashToken(bootstrapToken);

        var cluster = Cluster.Create(
            request.Name, request.Description, request.AgentPublicKeyPem,
            tokenHash, request.Labels);

        await db.Clusters.InsertOneAsync(cluster, cancellationToken: ct);
        logger.LogInformation("Cluster {Name} registered with id {Id}", cluster.Name, cluster.Id);

        return new RegisterClusterRestResponse(cluster.Id, bootstrapToken);
    }
}
