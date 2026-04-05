using Clustral.Agent.Proxy;
using Clustral.Sdk.Grpc;
using Clustral.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;

namespace Clustral.Agent.Tunnel;

/// <summary>
/// Manages the bidirectional gRPC tunnel stream to the ControlPlane.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunAsync"/> implements the outer reconnect loop with exponential
/// backoff + jitter.  Each call to <see cref="ConnectAndRunAsync"/> represents
/// one tunnel session: it opens the stream, sends <c>AgentHello</c>, then
/// dispatches incoming frames until the stream closes or an error occurs.
/// </para>
/// <para>
/// A background <c>HeartbeatAsync</c> task runs concurrently with the frame
/// dispatch loop and calls <c>ClusterService.UpdateStatus</c> periodically so
/// the ControlPlane can mark the cluster as <c>CONNECTED</c> while the tunnel
/// is alive.
/// </para>
/// </remarks>
public sealed class TunnelManager
{
    private readonly AgentOptions           _opts;
    private readonly AgentCredentialStore   _credentials;
    private readonly KubectlProxy           _proxy;
    private readonly ILogger<TunnelManager> _logger;

    private readonly Random _jitter = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public TunnelManager(
        IOptions<AgentOptions>   opts,
        AgentCredentialStore     credentials,
        KubectlProxy             proxy,
        ILogger<TunnelManager>   logger)
    {
        _opts        = opts.Value;
        _credentials = credentials;
        _proxy       = proxy;
        _logger      = logger;
    }

    /// <summary>
    /// Runs the reconnect loop until <paramref name="ct"/> is cancelled.
    /// Returns only on graceful shutdown.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var delay = _opts.Reconnect.InitialDelay;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Connecting to ControlPlane at {Url}", _opts.ControlPlaneUrl);
                await ConnectAndRunAsync(ct);

                // Clean disconnect (stream drained) — reset backoff.
                _logger.LogInformation("Tunnel closed cleanly; reconnecting immediately.");
                delay = _opts.Reconnect.InitialDelay;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Tunnel shutdown requested.");
                return;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
            {
                _logger.LogError("Authentication rejected by ControlPlane — check the agent credential.");
                // Don't hammer the server on auth failures; use the max delay.
                delay = _opts.Reconnect.MaxDelay;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tunnel error, reconnecting in {Delay}", delay);
            }

            if (ct.IsCancellationRequested) return;

            var jitter = TimeSpan.FromMilliseconds(
                _jitter.NextDouble() * _opts.Reconnect.MaxJitter.TotalMilliseconds);

            await Task.Delay(delay + jitter, ct).ConfigureAwait(false);

            delay = TimeSpan.FromMilliseconds(
                Math.Min(
                    delay.TotalMilliseconds * _opts.Reconnect.BackoffMultiplier,
                    _opts.Reconnect.MaxDelay.TotalMilliseconds));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Single tunnel session
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ConnectAndRunAsync(CancellationToken ct)
    {
        var token = await _credentials.ReadTokenAsync(ct)
            ?? throw new InvalidOperationException(
                "No agent credential found. Cannot open tunnel.");

        using var channel = CreateChannel(token);

        var tunnelClient  = new TunnelService.TunnelServiceClient(channel);
        var clusterClient = new ClusterService.ClusterServiceClient(channel);

        using var call = tunnelClient.OpenTunnel(cancellationToken: ct);

        // ── Handshake ─────────────────────────────────────────────────────────
        await call.RequestStream.WriteAsync(new TunnelClientMessage
        {
            Hello = new AgentHello
            {
                ClusterId          = _opts.ClusterId,
                AgentVersion       = _opts.AgentVersion,
                KubernetesVersion  = await DiscoverKubernetesVersionAsync(ct),
                SentAt             = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            },
        }, ct);

        // Wait for TunnelHello acknowledgement.
        if (!await call.ResponseStream.MoveNext(ct))
            throw new RpcException(new Status(StatusCode.Unavailable,
                "ControlPlane closed stream before sending TunnelHello."));

        var firstMsg = call.ResponseStream.Current;
        if (firstMsg.PayloadCase != TunnelServerMessage.PayloadOneofCase.Hello)
            throw new RpcException(new Status(StatusCode.Internal,
                $"Expected TunnelHello, got {firstMsg.PayloadCase}."));

        _logger.LogInformation(
            "Tunnel established with ControlPlane for cluster {ClusterId}",
            firstMsg.Hello.ClusterId);

        // ── Concurrent tasks: frame dispatch + heartbeat ──────────────────────
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var dispatchTask  = DispatchFramesAsync(call, linkedCts.Token);
        var heartbeatTask = HeartbeatAsync(clusterClient, linkedCts.Token);

        // If either task faults, cancel the other.
        var completed = await Task.WhenAny(dispatchTask, heartbeatTask);
        await linkedCts.CancelAsync();

        try { await dispatchTask; }  catch (OperationCanceledException) { /* expected */ }
        try { await heartbeatTask; } catch (OperationCanceledException) { /* expected */ }

        // Rethrow any real exception from the primary task.
        if (completed.IsFaulted)
            await completed; // re-throws
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Frame dispatch loop
    // ─────────────────────────────────────────────────────────────────────────

    private async Task DispatchFramesAsync(
        AsyncDuplexStreamingCall<TunnelClientMessage, TunnelServerMessage> call,
        CancellationToken ct)
    {
        await foreach (var msg in call.ResponseStream.ReadAllAsync(ct))
        {
            switch (msg.PayloadCase)
            {
                case TunnelServerMessage.PayloadOneofCase.HttpRequest:
                    // Fire-and-forget per request so multiple in-flight requests
                    // can proceed concurrently without blocking the receive loop.
                    _ = ProxyAndReplyAsync(call.RequestStream, msg.HttpRequest, ct);
                    break;

                case TunnelServerMessage.PayloadOneofCase.Ping:
                    await _writeLock.WaitAsync(ct);
                    try
                    {
                        await call.RequestStream.WriteAsync(new TunnelClientMessage
                        {
                            Pong = new PongFrame
                            {
                                Payload        = msg.Ping.Payload,
                                OriginalSentAt = msg.Ping.SentAt,
                            },
                        }, ct);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                    break;

                case TunnelServerMessage.PayloadOneofCase.Pong:
                    // RTT measurement placeholder.
                    break;

                case TunnelServerMessage.PayloadOneofCase.Cancel:
                    // TODO: cancel the in-flight ProxyAndReplyAsync for this request_id.
                    _logger.LogDebug("Cancel requested for {RequestId}", msg.Cancel.RequestId);
                    break;

                default:
                    _logger.LogWarning("Unexpected server message type {Type}", msg.PayloadCase);
                    break;
            }
        }

        // Normal stream end.
        await call.RequestStream.CompleteAsync();
    }

    private async Task ProxyAndReplyAsync(
        IClientStreamWriter<TunnelClientMessage> writer,
        HttpRequestFrame                         request,
        CancellationToken                        ct)
    {
        try
        {
            var response = await _proxy.ProxyAsync(request, ct);
            await _writeLock.WaitAsync(ct);
            try
            {
                await writer.WriteAsync(new TunnelClientMessage { HttpResponse = response }, ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Tunnel is shutting down; drop in-flight request silently.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error proxying {RequestId}", request.RequestId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Heartbeat
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HeartbeatAsync(
        ClusterService.ClusterServiceClient clusterClient,
        CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_opts.HeartbeatInterval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await clusterClient.UpdateStatusAsync(new UpdateClusterStatusRequest
                {
                    ClusterId         = _opts.ClusterId,
                    Status            = Clustral.V1.ClusterStatus.Connected,
                    KubernetesVersion = await DiscoverKubernetesVersionAsync(ct),
                }, cancellationToken: ct);
            }
            catch (RpcException ex)
            {
                // Heartbeat failure is non-fatal; the reconnect loop will handle
                // a lost connection via the dispatch task failing first.
                _logger.LogWarning(ex, "Heartbeat failed");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private GrpcChannel CreateChannel(string token)
    {
        var url = _opts.ControlPlaneUrl;

        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? GrpcChannelFactory.CreateInsecureWithToken(url, token)
            : GrpcChannelFactory.CreateWithToken(url, token);
    }

    /// <summary>
    /// Asks the local k8s API server for its version.
    /// Returns an empty string on failure so that a misconfigured cluster
    /// does not prevent the tunnel from connecting.
    /// </summary>
    private async Task<string> DiscoverKubernetesVersionAsync(CancellationToken ct)
    {
        // TODO: call GET /version on the k8s API server and parse GitVersion.
        // Returning empty for now; the ControlPlane treats empty as "unknown".
        return await Task.FromResult(string.Empty);
    }
}
