using Clustral.ControlPlane.Domain.Events;
using Clustral.Sdk.Crypto;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure.Auth;

/// <summary>
/// gRPC server interceptor that enforces mTLS + JWT on port 5443.
/// Validates JWT signature, cross-checks cert CN against JWT agent_id,
/// checks tokenVersion against MongoDB, and enforces allowedRpcs.
/// Publishes <see cref="AgentAuthFailed"/> audit events on every failure.
/// </summary>
public sealed class AgentAuthInterceptor(
    JwtIssuer jwtIssuer,
    ClustralDb db,
    IMemoryCache cache,
    IMediator mediator,
    ILogger<AgentAuthInterceptor> logger) : Interceptor
{
    private const int MtlsPort = 5443;
    private static readonly TimeSpan TokenVersionCacheTtl = TimeSpan.FromSeconds(30);

    // RPCs that skip mTLS+JWT (bootstrap — agent has no cert yet)
    private static readonly HashSet<string> BootstrapRpcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "/clustral.v1.ClusterService/RegisterAgent",
    };

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateIfRequired(context);
        return await continuation(request, context);
    }

    /// <summary>
    /// Validates mTLS + JWT if the request is on the mTLS port.
    /// Public so that duplex streaming handlers (e.g. TunnelServiceImpl.OpenTunnel)
    /// can call this directly, since gRPC interceptors don't support duplex streaming
    /// overrides cleanly in all versions.
    /// </summary>
    public async Task ValidateIfRequired(ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var localPort = httpContext.Connection.LocalPort;

        // Only enforce on the mTLS port
        if (localPort != MtlsPort) return;

        var method = context.Method;
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();

        // Bootstrap RPCs skip mTLS+JWT validation (agent has no cert yet)
        if (BootstrapRpcs.Contains(method)) return;

        // 1. Extract client certificate
        var clientCert = httpContext.Connection.ClientCertificate;
        if (clientCert is null)
        {
            PublishAuthFailed(null, "Client certificate required on port 5443", null, remoteIp);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Client certificate required on port 5443"));
        }

        var certCn = clientCert.GetNameInfo(
            System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, forIssuer: false);

        // 2. Extract JWT from authorization metadata
        var authHeader = context.RequestHeaders.GetValue("authorization");
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            PublishAuthFailed(null, "Bearer JWT required in authorization header", certCn, remoteIp);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Bearer JWT required in authorization header"));
        }
        var jwt = authHeader["Bearer ".Length..];

        // 3. Validate JWT signature
        System.Security.Claims.ClaimsPrincipal principal;
        try
        {
            principal = jwtIssuer.ValidateToken(jwt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWT validation failed for {Method}", method);
            PublishAuthFailed(null, "Invalid or expired JWT", certCn, remoteIp);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Invalid or expired JWT"));
        }

        // 4. Cross-check: JWT agent_id must match cert CN
        var jwtAgentId = JwtIssuer.GetAgentId(principal);
        if (string.IsNullOrEmpty(jwtAgentId) || !string.Equals(jwtAgentId, certCn, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("JWT agent_id {JwtAgentId} does not match cert CN {CertCn}", jwtAgentId, certCn);
            PublishAuthFailed(null, $"JWT agent_id '{jwtAgentId}' does not match certificate CN '{certCn}'", certCn, remoteIp);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "JWT agent_id does not match certificate CN"));
        }

        // 5. Check tokenVersion against MongoDB (cached)
        var jwtClusterId = JwtIssuer.GetClusterId(principal);
        if (string.IsNullOrEmpty(jwtClusterId) || !Guid.TryParse(jwtClusterId, out var clusterId))
        {
            PublishAuthFailed(null, "Invalid cluster_id in JWT", certCn, remoteIp);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Invalid cluster_id in JWT"));
        }

        var jwtTokenVersion = JwtIssuer.GetTokenVersion(principal);
        var storedVersion = await GetCachedTokenVersion(clusterId, context.CancellationToken);

        if (jwtTokenVersion < storedVersion)
        {
            logger.LogWarning("Token version mismatch for cluster {ClusterId}: JWT={JwtVersion}, stored={StoredVersion}",
                clusterId, jwtTokenVersion, storedVersion);
            PublishAuthFailed(clusterId, $"Token has been revoked (version {jwtTokenVersion} < {storedVersion})", certCn, remoteIp);
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Token has been revoked (version mismatch)"));
        }

        // 6. Check allowedRpcs
        var allowedRpcs = JwtIssuer.GetAllowedRpcs(principal);
        var shortMethod = method.Split('/').LastOrDefault() ?? method;
        var fullServiceMethod = method.TrimStart('/');

        if (allowedRpcs.Count > 0 &&
            !allowedRpcs.Any(r => fullServiceMethod.Contains(r, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("RPC {Method} not in allowedRpcs for agent {AgentId}", method, jwtAgentId);
            PublishAuthFailed(clusterId, $"RPC {shortMethod} is not permitted for this agent", certCn, remoteIp);
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"RPC {shortMethod} is not permitted for this agent"));
        }

        // Store validated identity in HttpContext for downstream use
        httpContext.Items["AgentId"] = jwtAgentId;
        httpContext.Items["ClusterId"] = clusterId;
    }

    private void PublishAuthFailed(Guid? clusterId, string reason, string? certCn, string? remoteIp)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await mediator.Publish(new AgentAuthFailed(clusterId, reason, certCn, remoteIp));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish agent auth failed event");
            }
        });
    }

    private async Task<int> GetCachedTokenVersion(Guid clusterId, CancellationToken ct)
    {
        var cacheKey = $"tokenVersion:{clusterId}";

        if (cache.TryGetValue<int>(cacheKey, out var version))
            return version;

        var cluster = await db.Clusters
            .Find(c => c.Id == clusterId)
            .FirstOrDefaultAsync(ct);

        var tokenVersion = cluster?.TokenVersion ?? 0;
        cache.Set(cacheKey, tokenVersion, TokenVersionCacheTtl);

        return tokenVersion;
    }
}
