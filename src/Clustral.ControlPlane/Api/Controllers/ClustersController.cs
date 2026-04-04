using System.Security.Cryptography;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Api.Controllers;

/// <summary>
/// REST endpoints for managing registered clusters.
/// These are consumed by the Web UI and CLI; agents use the gRPC
/// <c>ClusterService</c> directly.
/// </summary>
[ApiController]
[Route("api/v1/clusters")]
[Authorize]
public sealed class ClustersController(ClustralDb db, ILogger<ClustersController> logger)
    : ControllerBase
{
    // POST /api/v1/clusters
    [HttpPost]
    [ProducesResponseType<RegisterClusterRestResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterClusterRestRequest request,
        CancellationToken ct)
    {
        var exists = await db.Clusters
            .Find(c => c.Name == request.Name)
            .AnyAsync(ct);

        if (exists)
            return Conflict(new { error = $"A cluster named '{request.Name}' is already registered." });

        var bootstrapToken = GenerateToken();
        var tokenHash      = HashToken(bootstrapToken);

        var cluster = new Cluster
        {
            Id                 = Guid.NewGuid(),
            Name               = request.Name,
            Description        = request.Description,
            AgentPublicKeyPem  = request.AgentPublicKeyPem,
            BootstrapTokenHash = tokenHash,
            Status             = ClusterStatus.Pending,
            Labels             = request.Labels ?? new Dictionary<string, string>(),
        };

        await db.Clusters.InsertOneAsync(cluster, cancellationToken: ct);

        logger.LogInformation("Cluster {Name} registered with id {Id} via REST", cluster.Name, cluster.Id);

        return CreatedAtAction(nameof(Get), new { id = cluster.Id },
            new RegisterClusterRestResponse(cluster.Id, bootstrapToken));
    }

    // GET /api/v1/clusters
    [HttpGet]
    [ProducesResponseType<ClusterListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status       = null,
        [FromQuery] string? labelSelector = null,
        [FromQuery] int     pageSize      = 50,
        [FromQuery] string? pageToken     = null,
        CancellationToken ct = default)
    {
        var filter = Builders<Cluster>.Filter.Empty;

        // Status filter
        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<ClusterStatus>(status, ignoreCase: true, out var parsed))
        {
            filter &= Builders<Cluster>.Filter.Eq(c => c.Status, parsed);
        }

        // Simple cursor pagination: pageToken is a base64-encoded Guid (last seen Id).
        if (!string.IsNullOrEmpty(pageToken) &&
            TryDecodePageToken(pageToken, out var lastId))
        {
            filter &= Builders<Cluster>.Filter.Gt(c => c.Id, lastId);
        }

        pageSize = Math.Clamp(pageSize, 1, 200);
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
        return Ok(new ClusterListResponse(response, nextToken));
    }

    // GET /api/v1/clusters/{id}
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClusterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var cluster = await db.Clusters
            .Find(c => c.Id == id)
            .FirstOrDefaultAsync(ct);

        if (cluster is null)
            return NotFound();

        return Ok(ClusterResponse.From(cluster));
    }

    // DELETE /api/v1/clusters/{id}
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await db.Clusters.DeleteOneAsync(c => c.Id == id, ct);

        if (result.DeletedCount == 0)
            return NotFound();

        // Cascade: delete all access tokens for this cluster.
        await db.AccessTokens.DeleteManyAsync(t => t.ClusterId == id, ct);

        logger.LogInformation("Cluster {ClusterId} deregistered", id);
        return NoContent();
    }

    // -------------------------------------------------------------------------

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

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string raw)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
