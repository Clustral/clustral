using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Clustral.V1;

namespace Clustral.Agent.Proxy;

/// <summary>
/// Translates <see cref="HttpRequestFrame"/> messages received from the tunnel
/// into real HTTP requests against the Kubernetes API server and returns the
/// response as an <see cref="HttpResponseFrame"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Authorization model:</strong> the incoming <c>Authorization</c>
/// header (which carries the user's Clustral kubeconfig token) is stripped
/// before the request reaches the k8s API server.  The Agent authenticates
/// to the k8s API server using its own service account token.  Access control
/// at the k8s level is therefore governed by the agent's <c>ClusterRole</c>.
/// Fine-grained per-user RBAC enforcement is done at the ControlPlane before
/// requests enter the tunnel.
/// </para>
/// <para>
/// <strong>Streaming:</strong> the current implementation buffers the full
/// response body before returning a single <see cref="HttpResponseFrame"/>.
/// This is sufficient for regular kubectl commands but will not stream
/// <c>kubectl logs -f</c> or <c>kubectl exec</c> in real time.
/// Chunked streaming support (multiple frames per request) is a follow-up.
/// </para>
/// </remarks>
public sealed class KubectlProxy
{
    // Headers that must not be forwarded to the upstream k8s API server.
    private static readonly HashSet<string> _hopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade",
        // Strip user's Clustral token — agent presents its own identity to k8s.
        "Authorization",
        // Clustral internal headers — translated to k8s Impersonate-* below.
        "X-Clustral-Impersonate-User",
        "X-Clustral-Impersonate-Group",
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<KubectlProxy> _logger;

    public KubectlProxy(HttpClient httpClient, ILogger<KubectlProxy> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    /// <summary>
    /// Builds an HTTP request from <paramref name="frame"/>, executes it
    /// against the Kubernetes API server, and returns the response.
    /// </summary>
    public async Task<HttpResponseFrame> ProxyAsync(
        HttpRequestFrame frame,
        CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        try
        {
            using var request = BuildRequest(frame);
            response          = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            return await BuildResponseFrameAsync(frame.RequestId, response, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "k8s API request failed for {RequestId}: {Path}",
                frame.RequestId, frame.Head?.Path);

            return new HttpResponseFrame
            {
                RequestId = frame.RequestId,
                EndOfBody = true,
                Error     = new TunnelError
                {
                    Code    = TunnelErrorCode.TunnelErrorApiServerUnreachable,
                    Message = ex.Message,
                },
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error proxying {RequestId}", frame.RequestId);

            return new HttpResponseFrame
            {
                RequestId = frame.RequestId,
                EndOfBody = true,
                Error     = new TunnelError
                {
                    Code    = TunnelErrorCode.TunnelErrorUnspecified,
                    Message = $"Agent internal error: {ex.Message}",
                },
            };
        }
        finally
        {
            response?.Dispose();
        }
    }

    // -------------------------------------------------------------------------

    private HttpRequestMessage BuildRequest(HttpRequestFrame frame)
    {
        var head    = frame.Head ?? throw new InvalidOperationException(
            $"HttpRequestFrame {frame.RequestId} has no head.");
        var method  = new HttpMethod(head.Method);
        var request = new HttpRequestMessage(method, head.Path);

        // Translate Clustral impersonation headers to k8s Impersonation API headers.
        string? impersonateUser = null;
        var impersonateGroups = new List<string>();

        foreach (var h in head.Headers)
        {
            if (h.Name.Equals("X-Clustral-Impersonate-User", StringComparison.OrdinalIgnoreCase))
            {
                impersonateUser = h.Value;
                continue;
            }
            if (h.Name.Equals("X-Clustral-Impersonate-Group", StringComparison.OrdinalIgnoreCase))
            {
                impersonateGroups.Add(h.Value);
                continue;
            }

            if (_hopByHopHeaders.Contains(h.Name))
                continue;

            // Content-* headers must go on the content, not the request.
            if (h.Name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                continue;

            request.Headers.TryAddWithoutValidation(h.Name, h.Value);
        }

        // Set k8s Impersonation headers if the ControlPlane identified the user.
        if (impersonateUser is not null)
        {
            request.Headers.TryAddWithoutValidation("Impersonate-User", impersonateUser);
            foreach (var group in impersonateGroups)
                request.Headers.TryAddWithoutValidation("Impersonate-Group", group);
        }

        if (frame.BodyChunk.Length > 0 || method == HttpMethod.Post || method == HttpMethod.Put
            || method == HttpMethod.Patch)
        {
            var content = new ByteArrayContent(frame.BodyChunk.ToByteArray());
            foreach (var h in head.Headers)
            {
                if (h.Name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    content.Headers.TryAddWithoutValidation(h.Name, h.Value);
            }
            request.Content = content;
        }

        return request;
    }

    private static async Task<HttpResponseFrame> BuildResponseFrameAsync(
        string requestId,
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var body  = await response.Content.ReadAsByteArrayAsync(ct);
        var frame = new HttpResponseFrame
        {
            RequestId = requestId,
            Head      = new HttpResponseHead { StatusCode = (int)response.StatusCode },
            EndOfBody = true,
        };

        foreach (var (name, values) in response.Headers)
            frame.Head.Headers.Add(new HttpHeader { Name = name, Value = string.Join(", ", values) });

        foreach (var (name, values) in response.Content.Headers)
            frame.Head.Headers.Add(new HttpHeader { Name = name, Value = string.Join(", ", values) });

        if (body.Length > 0)
            frame.BodyChunk = Google.Protobuf.ByteString.CopyFrom(body);

        return frame;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Static factory
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="HttpClient"/> suitable for proxying to the
    /// Kubernetes API server.
    /// <list type="bullet">
    ///   <item><description>
    ///     <strong>In-cluster</strong>: uses the pod's service account token
    ///     (<c>/var/run/secrets/kubernetes.io/serviceaccount/token</c>) and
    ///     the cluster CA cert.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Outside cluster</strong>: connects to <paramref name="apiUrl"/>
    ///     with optional TLS verification skip (dev only).
    ///   </description></item>
    /// </list>
    /// </summary>
    public static HttpClient CreateKubernetesHttpClient(
        string apiUrl,
        bool   skipTlsVerify = false)
    {
        const string saTokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token";
        const string caCertPath  = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

        var isInCluster = File.Exists(saTokenPath);

        HttpMessageHandler innerHandler;

        if (isInCluster && !skipTlsVerify)
        {
            var caCert    = X509CertificateLoader.LoadCertificateFromFile(caCertPath);
            var socketsHandler = new SocketsHttpHandler();
            socketsHandler.SslOptions.RemoteCertificateValidationCallback =
                (_, cert, _, _) =>
                {
                    // Accept if the certificate is signed by the cluster CA.
                    if (cert is null) return false;
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode           = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(caCert);
                    chain.ChainPolicy.RevocationMode      = X509RevocationMode.NoCheck;
                    return chain.Build(new X509Certificate2(cert));
                };
            innerHandler = socketsHandler;
        }
        else if (skipTlsVerify)
        {
            innerHandler = new SocketsHttpHandler
            {
                SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            };
        }
        else
        {
            innerHandler = new SocketsHttpHandler();
        }

        HttpMessageHandler handler = isInCluster
            ? new ServiceAccountTokenHandler(saTokenPath, innerHandler)
            : innerHandler;

        return new HttpClient(handler) { BaseAddress = new Uri(apiUrl) };
    }
}

// =============================================================================
// ServiceAccountTokenHandler
// =============================================================================

/// <summary>
/// Attaches the pod's service account bearer token to every outgoing request.
/// The token file is re-read on each request because kubelet rotates it
/// periodically (typically every hour).
/// </summary>
internal sealed class ServiceAccountTokenHandler : DelegatingHandler
{
    private readonly string _tokenPath;

    internal ServiceAccountTokenHandler(string tokenPath, HttpMessageHandler inner)
        : base(inner)
    {
        _tokenPath = tokenPath;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken  cancellationToken)
    {
        var token = await File.ReadAllTextAsync(_tokenPath, cancellationToken);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Trim());

        return await base.SendAsync(request, cancellationToken);
    }
}
