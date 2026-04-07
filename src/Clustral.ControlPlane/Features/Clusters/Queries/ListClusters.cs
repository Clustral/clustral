using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.Clusters.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

public record ListClustersQuery(
    string? StatusFilter,
    int PageSize,
    string? PageToken) : IQuery<Result<ClusterListResponse>>;

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class ListClustersHandler(IClusterRepository clusters)
    : IRequestHandler<ListClustersQuery, Result<ClusterListResponse>>
{
    public async Task<Result<ClusterListResponse>> Handle(ListClustersQuery request, CancellationToken ct)
    {
        var allClusters = await clusters.ListAsync(ct);

        IEnumerable<Cluster> filtered = allClusters;

        if (!string.IsNullOrEmpty(request.StatusFilter) &&
            Enum.TryParse<ClusterStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
        {
            filtered = filtered.Where(c => c.Status == parsed);
        }

        filtered = filtered.OrderBy(c => c.Id);

        if (!string.IsNullOrEmpty(request.PageToken) &&
            TryDecodePageToken(request.PageToken, out var lastId))
        {
            filtered = filtered.Where(c => c.Id.CompareTo(lastId) > 0);
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = filtered.Take(pageSize + 1).ToList();

        string? nextToken = null;
        if (page.Count > pageSize)
        {
            page.RemoveAt(page.Count - 1);
            nextToken = EncodePageToken(page[^1].Id);
        }

        var response = page.Select(ClusterResponse.From).ToList();
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
