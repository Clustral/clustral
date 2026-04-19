using Clustral.Internal.V1;
using Clustral.V1;
using Grpc.Net.Client;

namespace Clustral.ControlPlane.Infrastructure.Tunnel;

/// <summary>
/// Forwards kubectl HTTP requests to the TunnelService pod that holds
/// the active agent tunnel session for a given cluster.
/// </summary>
public interface ITunnelProxyClient
{
    Task<HttpResponseFrame> ProxyRequestAsync(
        string tunnelPod, Guid clusterId, HttpRequestFrame frame, CancellationToken ct);
}

public sealed class TunnelProxyClient : ITunnelProxyClient
{
    private readonly string _baseUrl;

    public TunnelProxyClient(IConfiguration config)
    {
        _baseUrl = config["TunnelService:Url"] ?? "http://localhost:50051";
    }

    public async Task<HttpResponseFrame> ProxyRequestAsync(
        string tunnelPod, Guid clusterId, HttpRequestFrame frame, CancellationToken ct)
    {
        // For single-instance (Docker Compose): use _baseUrl directly.
        // For k8s StatefulSet: address would be {tunnelPod}.tunnel-service.{namespace}:50051
        // For now, use the configured base URL (single-instance).
        using var channel = GrpcChannel.ForAddress(_baseUrl);
        var client = new TunnelProxy.TunnelProxyClient(channel);

        var response = await client.ProxyRequestAsync(new TunnelProxyRequest
        {
            ClusterId = clusterId.ToString(),
            Frame = frame,
        }, cancellationToken: ct);

        return response.Frame;
    }
}
