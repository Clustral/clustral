using System.Collections.Concurrent;
using System.Reflection;
using Clustral.ControlPlane.Infrastructure;
using Clustral.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Protos;

/// <summary>
/// gRPC server implementation of <c>TunnelService</c>.
/// Each Agent opens one persistent bidirectional stream; kubectl traffic
/// is multiplexed over that stream using the request_id framing protocol
/// defined in <c>tunnel.proto</c>.
/// </summary>
public sealed class TunnelServiceImpl(
    ClustralDb db,
    TunnelSessionManager sessions,
    Infrastructure.Auth.AgentAuthInterceptor agentAuth,
    ILogger<TunnelServiceImpl> logger)
    : TunnelService.TunnelServiceBase
{
    public override async Task OpenTunnel(
        IAsyncStreamReader<TunnelClientMessage>  requestStream,
        IServerStreamWriter<TunnelServerMessage> responseStream,
        ServerCallContext context)
    {
        // mTLS + JWT validation for duplex streams (interceptor only handles unary).
        await agentAuth.ValidateIfRequired(context);

        // ── 1. Extract cluster ID from validated JWT claims ──────────────────
        var httpContext = context.GetHttpContext();
        if (!httpContext.Items.TryGetValue("ClusterId", out var clusterIdObj) || clusterIdObj is not Guid clusterId)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Agent authentication required (mTLS + JWT on port 5443)."));
        }

        // ── 2. Handshake ─────────────────────────────────────────────────────
        if (!await requestStream.MoveNext(context.CancellationToken))
            throw new RpcException(new Status(StatusCode.Cancelled, "Stream closed before handshake."));

        var firstMsg = requestStream.Current;
        if (firstMsg.PayloadCase != TunnelClientMessage.PayloadOneofCase.Hello)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Expected AgentHello as the first message."));

        var hello = firstMsg.Hello;

        logger.LogInformation(
            "Agent tunnel opened for cluster {ClusterId} (agent v{AgentVersion}, k8s {K8sVersion})",
            clusterId, hello.AgentVersion, hello.KubernetesVersion);

        await responseStream.WriteAsync(new TunnelServerMessage
        {
            Hello = new TunnelHello
            {
                ClusterId  = clusterId.ToString(),
                ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            },
        }, context.CancellationToken);

        // ── 3. Mark cluster as Connected + store agent version ────────────────
        var connectedUpdate = Builders<Domain.Cluster>.Update
            .Set(c => c.Status, Domain.ClusterStatus.Connected)
            .Set(c => c.KubernetesVersion, hello.KubernetesVersion)
            .Set(c => c.AgentVersion, hello.AgentVersion)
            .Set(c => c.LastSeenAt, DateTimeOffset.UtcNow);
        await db.Clusters.UpdateOneAsync(
            c => c.Id == clusterId, connectedUpdate,
            cancellationToken: context.CancellationToken);

        // Version compatibility check.
        var controlPlaneVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0-dev";
        if (!string.IsNullOrEmpty(hello.AgentVersion) && hello.AgentVersion != controlPlaneVersion)
        {
            logger.LogWarning(
                "Agent version mismatch for cluster {ClusterId}: agent={AgentVersion}, controlPlane={ControlPlaneVersion}",
                clusterId, hello.AgentVersion, controlPlaneVersion);
        }

        // ── 4. Register session ───────────────────────────────────────────────
        using var session = sessions.Register(clusterId, responseStream, context);

        try
        {
            // ── 5. Process incoming frames ────────────────────────────────────
            await foreach (var msg in requestStream.ReadAllAsync(context.CancellationToken))
            {
                // Update last-seen on every message.
                await db.Clusters.UpdateOneAsync(
                    c => c.Id == clusterId,
                    Builders<Domain.Cluster>.Update.Set(c => c.LastSeenAt, DateTimeOffset.UtcNow),
                    cancellationToken: context.CancellationToken);

                switch (msg.PayloadCase)
                {
                    case TunnelClientMessage.PayloadOneofCase.HttpResponse:
                        session.HandleHttpResponse(msg.HttpResponse);
                        break;

                    case TunnelClientMessage.PayloadOneofCase.Ping:
                        await responseStream.WriteAsync(new TunnelServerMessage
                        {
                            Pong = new PongFrame
                            {
                                Payload          = msg.Ping.Payload,
                                OriginalSentAt   = msg.Ping.SentAt,
                            },
                        }, context.CancellationToken);
                        break;

                    case TunnelClientMessage.PayloadOneofCase.Pong:
                        // RTT measurement — no-op for now.
                        break;

                    default:
                        logger.LogWarning("Unexpected payload type {Type} from agent, ignoring.",
                            msg.PayloadCase);
                        break;
                }
            }
        }
        finally
        {
            // ── 6. Agent disconnected — mark as Disconnected ──────────────────
            logger.LogInformation("Agent tunnel closed for cluster {ClusterId}", clusterId);

            var disconnectedUpdate = Builders<Domain.Cluster>.Update
                .Set(c => c.Status, Domain.ClusterStatus.Disconnected)
                .Set(c => c.LastSeenAt, DateTimeOffset.UtcNow);
            await db.Clusters.UpdateOneAsync(c => c.Id == clusterId, disconnectedUpdate);
        }
    }

}

// =============================================================================
// TunnelSession — tracks one active agent tunnel stream
// =============================================================================

/// <summary>
/// Represents a single active tunnel connection from an Agent.
/// Registered in <see cref="TunnelSessionManager"/> for the duration of
/// the bidirectional gRPC stream.
/// </summary>
public sealed class TunnelSession : IDisposable
{
    private readonly Guid _clusterId;
    private readonly IServerStreamWriter<TunnelServerMessage> _writer;
    private readonly TunnelSessionManager _manager;

    // In-flight requests: request_id → completion source for the response.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<HttpResponseFrame>>
        _pendingRequests = new();

    internal TunnelSession(
        Guid clusterId,
        IServerStreamWriter<TunnelServerMessage> writer,
        TunnelSessionManager manager)
    {
        _clusterId = clusterId;
        _writer    = writer;
        _manager   = manager;
    }

    /// <summary>
    /// Sends one HTTP request frame to the agent and waits for the response.
    /// </summary>
    public async Task<HttpResponseFrame> ProxyAsync(
        HttpRequestFrame request,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<HttpResponseFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingRequests[request.RequestId] = tcs;

        try
        {
            await _writer.WriteAsync(
                new TunnelServerMessage { HttpRequest = request }, ct);

            return await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            _pendingRequests.TryRemove(request.RequestId, out _);
        }
    }

    internal void HandleHttpResponse(HttpResponseFrame frame)
    {
        if (_pendingRequests.TryGetValue(frame.RequestId, out var tcs))
            tcs.TrySetResult(frame);
    }

    public void Dispose() => _manager.Unregister(_clusterId);
}

// =============================================================================
// TunnelSessionManager — singleton registry
// =============================================================================

/// <summary>
/// Singleton that maps cluster IDs to their active <see cref="TunnelSession"/>.
/// The kubectl proxy handler resolves the session here to forward traffic.
/// </summary>
public sealed class TunnelSessionManager
{
    private readonly ConcurrentDictionary<Guid, TunnelSession> _sessions = new();

    internal TunnelSession Register(
        Guid clusterId,
        IServerStreamWriter<TunnelServerMessage> writer,
        ServerCallContext context)
    {
        var session = new TunnelSession(clusterId, writer, this);
        _sessions[clusterId] = session;
        return session;
    }

    internal void Unregister(Guid clusterId) =>
        _sessions.TryRemove(clusterId, out _);

    public TunnelSession? GetSession(Guid clusterId) =>
        _sessions.TryGetValue(clusterId, out var s) ? s : null;

    public bool IsConnected(Guid clusterId) => _sessions.ContainsKey(clusterId);
}
