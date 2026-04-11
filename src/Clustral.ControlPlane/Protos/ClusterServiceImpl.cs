using System.Security.Cryptography;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Crypto;
using Clustral.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MongoDB.Driver;
using DomainCluster       = Clustral.ControlPlane.Domain.Cluster;
using DomainClusterStatus = Clustral.ControlPlane.Domain.ClusterStatus;

namespace Clustral.ControlPlane.Protos;

/// <summary>
/// gRPC server implementation of <c>ClusterService</c>.
/// Called by the Agent on boot (<c>Register</c>) and by the Web UI / CLI
/// indirectly through the REST layer.
/// </summary>
public sealed class ClusterServiceImpl(
    ClustralDb db,
    CertificateAuthority ca,
    JwtIssuer jwtIssuer,
    ILogger<ClusterServiceImpl> logger)
    : ClusterService.ClusterServiceBase
{
    private static readonly IReadOnlyList<string> DefaultAllowedRpcs =
    [
        "ClusterService/RegisterAgent",
        "ClusterService/UpdateStatus",
        "ClusterService/RenewCertificate",
        "ClusterService/RenewToken",
        "TunnelService/OpenTunnel",
    ];
    public override async Task<RegisterClusterResponse> Register(
        RegisterClusterRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "name is required"));

        // Reject duplicate names.
        var exists = await db.Clusters
            .Find(c => c.Name == request.Name)
            .AnyAsync(context.CancellationToken);

        if (exists)
            throw new RpcException(new Status(StatusCode.AlreadyExists,
                $"A cluster named '{request.Name}' is already registered."));

        // Generate a one-time bootstrap token.
        var bootstrapToken = GenerateToken();
        var tokenHash      = HashToken(bootstrapToken);

        var cluster = new DomainCluster
        {
            Id                 = Guid.NewGuid(),
            Name               = request.Name,
            Description        = request.Description,
            BootstrapTokenHash = tokenHash,
            Status             = DomainClusterStatus.Pending,
            Labels             = request.Labels.ToDictionary(kv => kv.Key, kv => kv.Value),
        };

        await db.Clusters.InsertOneAsync(cluster, cancellationToken: context.CancellationToken);

        logger.LogInformation("Cluster {Name} registered with id {Id}", cluster.Name, cluster.Id);

        return new RegisterClusterResponse
        {
            ClusterId      = cluster.Id.ToString(),
            BootstrapToken = bootstrapToken,
        };
    }

    public override async Task<ListClustersResponse> List(
        ListClustersRequest request,
        ServerCallContext context)
    {
        var filter = Builders<DomainCluster>.Filter.Empty;

        // Status filter
        if (request.StatusFilter != ClusterStatus.Unspecified)
        {
            var domainStatus = MapStatus(request.StatusFilter);
            filter &= Builders<DomainCluster>.Filter.Eq(c => c.Status, domainStatus);
        }

        // Label selector (AND semantics — all supplied labels must match)
        foreach (var (key, value) in request.LabelSelector)
        {
            filter &= Builders<DomainCluster>.Filter.Eq($"Labels.{key}", value);
        }

        var pageSize = request.PageSize > 0 ? Math.Min(request.PageSize, 200) : 50;
        var clusters = await db.Clusters
            .Find(filter)
            .SortBy(c => c.Id)
            .Limit(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListClustersResponse();
        response.Clusters.AddRange(clusters.Select(ToProto));
        return response;
    }

    public override async Task<Cluster> Get(
        GetClusterRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cluster_id must be a valid UUID"));

        var cluster = await db.Clusters
            .Find(c => c.Id == id)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (cluster is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Cluster {request.ClusterId} not found"));

        return ToProto(cluster);
    }

    public override async Task<Empty> UpdateStatus(
        UpdateClusterStatusRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cluster_id must be a valid UUID"));

        // KubernetesVersion is set once in AgentHello (TunnelServiceImpl),
        // not on every heartbeat. Heartbeat only updates status + last seen.
        var update = Builders<DomainCluster>.Update
            .Set(c => c.Status, MapStatus(request.Status))
            .Set(c => c.LastSeenAt, DateTimeOffset.UtcNow);

        var result = await db.Clusters.UpdateOneAsync(
            c => c.Id == id, update, cancellationToken: context.CancellationToken);

        if (result.MatchedCount == 0)
            throw new RpcException(new Status(StatusCode.NotFound, $"Cluster {request.ClusterId} not found"));

        return new Empty();
    }

    public override async Task<Empty> Deregister(
        DeregisterClusterRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cluster_id must be a valid UUID"));

        var result = await db.Clusters.DeleteOneAsync(
            c => c.Id == id, context.CancellationToken);

        if (result.DeletedCount == 0)
            throw new RpcException(new Status(StatusCode.NotFound, $"Cluster {request.ClusterId} not found"));

        // Cascade: delete all access tokens for this cluster.
        await db.AccessTokens.DeleteManyAsync(
            t => t.ClusterId == id, context.CancellationToken);

        logger.LogInformation("Cluster {ClusterId} deregistered via gRPC", id);
        return new Empty();
    }

    // ── RegisterAgent ─────────────────────────────────────────────────────────

    public override async Task<RegisterAgentResponse> RegisterAgent(
        RegisterAgentRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var clusterId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cluster_id must be a valid UUID"));

        if (string.IsNullOrWhiteSpace(request.BootstrapToken))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "bootstrap_token is required"));

        var cluster = await db.Clusters
            .Find(c => c.Id == clusterId)
            .FirstOrDefaultAsync(context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Cluster {request.ClusterId} not found"));

        // Verify bootstrap token
        if (string.IsNullOrEmpty(cluster.BootstrapTokenHash))
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Bootstrap token has already been consumed. Re-register the cluster to generate a new one."));

        var incomingHash = HashToken(request.BootstrapToken);
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(incomingHash),
                System.Text.Encoding.UTF8.GetBytes(cluster.BootstrapTokenHash)))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid bootstrap token"));
        }

        // Consume the bootstrap token
        cluster.ConsumeBootstrapToken();

        // Issue client certificate
        var agentId = clusterId.ToString();
        var orgId = "clustral"; // default org for single-tenant deployments
        var (certPem, keyPem) = ca.IssueCertificate(agentId, agentId, orgId);

        // Issue JWT
        var jwt = jwtIssuer.IssueToken(agentId, orgId, agentId, DefaultAllowedRpcs, cluster.TokenVersion);
        var jwtExpiry = jwtIssuer.GetTokenExpiry();

        // Parse cert to get fingerprint and expiry
        var clientCert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(certPem);
        cluster.RecordCertificateFingerprint(CertificateAuthority.Fingerprint(clientCert));

        await db.Clusters.ReplaceOneAsync(
            c => c.Id == clusterId, cluster, cancellationToken: context.CancellationToken);

        logger.LogInformation("Agent registered for cluster {ClusterId} with mTLS + JWT", clusterId);

        return new RegisterAgentResponse
        {
            ClusterId = clusterId.ToString(),
            ClientCertificatePem = certPem,
            ClientPrivateKeyPem = keyPem,
            CaCertificatePem = ca.GetCaCertificatePem(),
            Jwt = jwt,
            CertExpiresAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(clientCert.NotAfter.ToUniversalTime(), TimeSpan.Zero)),
            JwtExpiresAt = Timestamp.FromDateTimeOffset(jwtExpiry),
        };
    }

    // ── RenewCertificate ─────────────────────────────────────────────────────

    public override async Task<RenewCertificateResponse> RenewCertificate(
        RenewCertificateRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var clusterId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cluster_id must be a valid UUID"));

        var cluster = await db.Clusters
            .Find(c => c.Id == clusterId)
            .FirstOrDefaultAsync(context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Cluster {request.ClusterId} not found"));

        var agentId = clusterId.ToString();
        var orgId = "clustral";
        var (certPem, keyPem) = ca.IssueCertificate(agentId, agentId, orgId);

        var clientCert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(certPem);
        cluster.RecordCertificateFingerprint(CertificateAuthority.Fingerprint(clientCert));

        await db.Clusters.ReplaceOneAsync(
            c => c.Id == clusterId, cluster, cancellationToken: context.CancellationToken);

        logger.LogInformation("Certificate renewed for cluster {ClusterId}", clusterId);

        return new RenewCertificateResponse
        {
            ClientCertificatePem = certPem,
            ClientPrivateKeyPem = keyPem,
            ExpiresAt = Timestamp.FromDateTimeOffset(new DateTimeOffset(clientCert.NotAfter.ToUniversalTime(), TimeSpan.Zero)),
        };
    }

    // ── RenewToken ───────────────────────────────────────────────────────────

    public override async Task<RenewTokenResponse> RenewToken(
        RenewTokenRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ClusterId, out var clusterId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cluster_id must be a valid UUID"));

        var cluster = await db.Clusters
            .Find(c => c.Id == clusterId)
            .FirstOrDefaultAsync(context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Cluster {request.ClusterId} not found"));

        var agentId = clusterId.ToString();
        var orgId = "clustral";
        var jwt = jwtIssuer.IssueToken(agentId, orgId, agentId, DefaultAllowedRpcs, cluster.TokenVersion);
        var jwtExpiry = jwtIssuer.GetTokenExpiry();

        logger.LogInformation("JWT renewed for cluster {ClusterId} (tokenVersion={Version})",
            clusterId, cluster.TokenVersion);

        return new RenewTokenResponse
        {
            Jwt = jwt,
            ExpiresAt = Timestamp.FromDateTimeOffset(jwtExpiry),
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Cluster ToProto(DomainCluster c)
    {
        var proto = new Cluster
        {
            Id                = c.Id.ToString(),
            Name              = c.Name,
            Description       = c.Description,
            KubernetesVersion = c.KubernetesVersion ?? string.Empty,
            Status            = MapStatus(c.Status),
            RegisteredAt      = Timestamp.FromDateTimeOffset(c.RegisteredAt),
        };
        if (c.LastSeenAt.HasValue)
            proto.LastSeenAt = Timestamp.FromDateTimeOffset(c.LastSeenAt.Value);
        proto.Labels.Add(c.Labels);
        return proto;
    }

    private static ClusterStatus MapStatus(DomainClusterStatus s) => s switch
    {
        DomainClusterStatus.Pending      => ClusterStatus.Pending,
        DomainClusterStatus.Connected    => ClusterStatus.Connected,
        DomainClusterStatus.Disconnected => ClusterStatus.Disconnected,
        _                                => ClusterStatus.Unspecified,
    };

    private static DomainClusterStatus MapStatus(ClusterStatus s) => s switch
    {
        ClusterStatus.Pending      => DomainClusterStatus.Pending,
        ClusterStatus.Connected    => DomainClusterStatus.Connected,
        ClusterStatus.Disconnected => DomainClusterStatus.Disconnected,
        _                                      => DomainClusterStatus.Pending,
    };

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string raw)
    {
        var hash = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
