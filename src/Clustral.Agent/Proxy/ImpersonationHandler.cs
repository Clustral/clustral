using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Clustral.Agent.Proxy;

/// <summary>
/// Raw HTTP handler that sends k8s Impersonate-Group headers as separate
/// header lines. .NET HttpClient combines multi-value headers into a single
/// comma-separated line (confirmed by tests), but k8s requires each group
/// as a separate header line.
///
/// This handler bypasses HttpClient's header serialization by writing raw
/// HTTP/1.1 directly to the socket with proper TLS and CA cert handling.
/// Response parsing handles chunked transfer encoding and content-length.
/// </summary>
internal sealed class ImpersonationHandler : HttpMessageHandler
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useTls;
    private readonly bool _skipTlsVerify;
    private readonly X509Certificate2? _caCert;

    private readonly string? _saTokenPath;

    internal ImpersonationHandler(Uri baseAddress, bool skipTlsVerify, string? saTokenPath = null)
    {
        _host = baseAddress.Host;
        _port = baseAddress.Port;
        _useTls = baseAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        _skipTlsVerify = skipTlsVerify;
        _saTokenPath = saTokenPath;

        const string caCertPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";
        if (_useTls && !skipTlsVerify && File.Exists(caCertPath))
            _caCert = X509CertificateLoader.LoadCertificateFromFile(caCertPath);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, ct);

        Stream stream = tcp.GetStream();

        if (_useTls)
        {
            RemoteCertificateValidationCallback? certCallback = null;

            if (_skipTlsVerify)
            {
                certCallback = (_, _, _, _) => true;
            }
            else if (_caCert is not null)
            {
                certCallback = (_, cert, _, _) =>
                {
                    if (cert is null) return false;
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(_caCert);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build(new X509Certificate2(cert));
                };
            }

            var sslStream = new SslStream(stream, false, certCallback);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _host,
                RemoteCertificateValidationCallback = certCallback,
            }, ct);
            stream = sslStream;
        }

        // Read body.
        byte[]? body = null;
        if (request.Content is not null)
            body = await request.Content.ReadAsByteArrayAsync(ct);

        // Inject SA token if running in-cluster.
        if (_saTokenPath is not null && File.Exists(_saTokenPath))
        {
            var token = (await File.ReadAllTextAsync(_saTokenPath, ct)).Trim();
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        // Build raw HTTP/1.1 request with SEPARATE Impersonate-Group headers.
        var sb = new StringBuilder();
        sb.Append($"{request.Method} {uri.PathAndQuery} HTTP/1.1\r\n");
        sb.Append($"Host: {_host}:{_port}\r\n");
        sb.Append("Connection: close\r\n");

        foreach (var header in request.Headers)
        {
            var key = header.Key;

            // Skip headers we handle ourselves.
            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                continue;

            if (key.Equals("Impersonate-Group", StringComparison.OrdinalIgnoreCase))
            {
                // THE FIX: write each group as a separate header line.
                foreach (var value in header.Value)
                    sb.Append($"Impersonate-Group: {value}\r\n");
            }
            else
            {
                sb.Append($"{key}: {string.Join(", ", header.Value)}\r\n");
            }
        }

        // Content headers.
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    sb.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
            }
        }

        if (body is not null)
            sb.Append($"Content-Length: {body.Length}\r\n");

        sb.Append("\r\n");

        // Send request.
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
        if (body is not null && body.Length > 0)
            await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);

        // Read response.
        return await ReadResponseAsync(stream, ct);
    }

    private static async Task<HttpResponseMessage> ReadResponseAsync(Stream stream, CancellationToken ct)
    {
        // Read all bytes (Connection: close ensures the server closes after response).
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
            ms.Write(buf, 0, read);

        var raw = ms.ToArray();
        var text = Encoding.ASCII.GetString(raw);

        // Find header/body boundary.
        var headerEndIdx = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEndIdx < 0)
            return new HttpResponseMessage(HttpStatusCode.BadGateway);

        var headerSection = text[..headerEndIdx];
        var bodyStart = headerEndIdx + 4; // skip \r\n\r\n

        // Parse status line.
        var lines = headerSection.Split("\r\n");
        var statusParts = lines[0].Split(' ', 3);
        var statusCode = statusParts.Length >= 2 ? int.Parse(statusParts[1]) : 200;

        var response = new HttpResponseMessage((HttpStatusCode)statusCode);

        // Parse headers.
        var isChunked = false;
        var contentLength = -1L;

        for (int i = 1; i < lines.Length; i++)
        {
            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx <= 0) continue;
            var name = lines[i][..colonIdx].Trim();
            var value = lines[i][(colonIdx + 1)..].Trim();

            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                isChunked = true;

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = long.Parse(value);

            response.Headers.TryAddWithoutValidation(name, value);
        }

        // Extract body bytes.
        var bodyBytes = raw[bodyStart..];

        if (isChunked)
            bodyBytes = DecodeChunked(bodyBytes);

        if (bodyBytes.Length > 0)
            response.Content = new ByteArrayContent(bodyBytes);
        else
            response.Content = new ByteArrayContent([]);

        return response;
    }

    private static byte[] DecodeChunked(byte[] data)
    {
        using var result = new MemoryStream();
        var text = Encoding.ASCII.GetString(data);
        var pos = 0;

        while (pos < text.Length)
        {
            var lineEnd = text.IndexOf("\r\n", pos, StringComparison.Ordinal);
            if (lineEnd < 0) break;

            var sizeLine = text[pos..lineEnd].Trim();
            if (!int.TryParse(sizeLine, System.Globalization.NumberStyles.HexNumber, null, out var chunkSize))
                break;

            if (chunkSize == 0)
                break; // Last chunk.

            var chunkStart = lineEnd + 2; // skip \r\n after size
            if (chunkStart + chunkSize > data.Length)
            {
                // Partial chunk — write what we have.
                result.Write(data, chunkStart, data.Length - chunkStart);
                break;
            }

            result.Write(data, chunkStart, chunkSize);
            pos = chunkStart + chunkSize + 2; // skip chunk data + \r\n
        }

        return result.ToArray();
    }
}
