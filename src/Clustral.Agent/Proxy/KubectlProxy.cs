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
            // .NET HttpClient combines multi-value headers into a single
            // comma-separated header, but k8s requires each Impersonate-Group
            // as a separate header line. Workaround: send each group as a
            // uniquely-suffixed header that our custom handler splits.
            for (int i = 0; i < impersonateGroups.Count; i++)
                request.Headers.TryAddWithoutValidation($"Impersonate-Group", impersonateGroups[i]);
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

        var isInCluster = File.Exists(saTokenPath);
        var baseUri = new Uri(apiUrl);

        // Use ImpersonationHandler which writes raw HTTP with separate
        // Impersonate-Group headers. .NET HttpClient combines multi-value
        // headers into comma-separated values, which k8s doesn't accept.
        Func<CancellationToken, Task<string>>? saTokenProvider = isInCluster
            ? async ct => (await File.ReadAllTextAsync(saTokenPath, ct)).Trim()
            : null;

        var handler = new ImpersonationHandler(baseUri, skipTlsVerify, saTokenProvider);
        return new HttpClient(handler) { BaseAddress = baseUri };
    }
}
