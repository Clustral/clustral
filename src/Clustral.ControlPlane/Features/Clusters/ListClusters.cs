using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Clusters;

// ── Query ────────────────────────────────────────────────────────────────────

public record ListClustersQuery(
    string? StatusFilter,
    int PageSize,
    string? PageToken) : IRequest<ClusterListResponse>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class ListClustersHandler(ClustralDb db)
    : IRequestHandler<ListClustersQuery, ClusterListResponse>
{
    public async Task<ClusterListResponse> Handle(ListClustersQuery request, CancellationToken ct)
    {
        var filter = Builders<Cluster>.Filter.Empty;

        if (!string.IsNullOrEmpty(request.StatusFilter) &&
            Enum.TryParse<ClusterStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
        {
            filter &= Builders<Cluster>.Filter.Eq(c => c.Status, parsed);
        }

        if (!string.IsNullOrEmpty(request.PageToken) &&
            TryDecodePageToken(request.PageToken, out var lastId))
        {
            filter &= Builders<Cluster>.Filter.Gt(c => c.Id, lastId);
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var clusters = await db.Clusters
            .Find(filter)
            .SortBy(c => c.Id)
            .Limit(pageSize + 1)
            .ToListAsync(ct);

        string? nextToken = null;
        if (clusters.Count > pageSize)
        {
            clusters.RemoveAt(clusters.Count - 1);
            nextToken = EncodePageToken(clusters[^1].Id);
        }

        var response = clusters.Select(ClusterResponse.From).ToList();
        return new ClusterListResponse(response, nextToken);
    }

    private static bool TryDecodePageToken(string token, out Guid id)
    {
        try
        {
            var bytes = Convert.FromBase64String(token);
            id = new Guid(bytes);
            return true;
        }
        catch
        {
            id = default;
            return false;
        }
    }

    private static string EncodePageToken(Guid id) =>
        Convert.ToBase64String(id.ToByteArray());
}
