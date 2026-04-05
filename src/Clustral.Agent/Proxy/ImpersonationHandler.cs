using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Clustral.Agent.Proxy;

/// <summary>
/// Sends HTTP requests with separate Impersonate-Group header lines.
///
/// .NET HttpClient combines multi-value headers into a single
/// comma-separated line, but k8s requires each Impersonate-Group
/// as a separate header. This handler bypasses HttpClient's serialization
/// by writing the raw HTTP request directly to the socket when
/// impersonation headers are present.
/// </summary>
internal sealed class ImpersonationHandler : HttpMessageHandler
{
    private readonly Uri _baseAddress;
    private readonly bool _skipTlsVerify;
    private readonly Func<CancellationToken, Task<string>>? _saTokenProvider;

    internal ImpersonationHandler(
        Uri baseAddress,
        bool skipTlsVerify,
        Func<CancellationToken, Task<string>>? saTokenProvider = null)
    {
        _baseAddress = baseAddress;
        _skipTlsVerify = skipTlsVerify;
        _saTokenProvider = saTokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var uri = request.RequestUri ?? new Uri(_baseAddress, request.RequestUri?.PathAndQuery ?? "/");
        var host = _baseAddress.Host;
        var port = _baseAddress.Port;
        var useTls = _baseAddress.Scheme == "https";

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);

        Stream stream = tcp.GetStream();
        if (useTls)
        {
            var sslStream = new SslStream(stream, false, (_, _, _, _) => _skipTlsVerify || true);
            await sslStream.AuthenticateAsClientAsync(host);
            stream = sslStream;
        }

        // Read body if present.
        byte[]? body = null;
        if (request.Content is not null)
            body = await request.Content.ReadAsByteArrayAsync(ct);

        // Build raw HTTP request with separate headers.
        var sb = new StringBuilder();
        sb.Append($"{request.Method} {uri.PathAndQuery} HTTP/1.1\r\n");
        sb.Append($"Host: {host}:{port}\r\n");
        sb.Append("Connection: close\r\n");

        // Add SA token if available.
        if (_saTokenProvider is not null)
        {
            var token = await _saTokenProvider(ct);
            sb.Append($"Authorization: Bearer {token}\r\n");
        }

        // Write each header individually — the key part.
        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Impersonate-Group", StringComparison.OrdinalIgnoreCase))
            {
                // Write EACH group as a SEPARATE header line.
                foreach (var value in header.Value)
                    sb.Append($"Impersonate-Group: {value}\r\n");
            }
            else if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                     !header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) &&
                     !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
            }
        }

        // Content headers.
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
                sb.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
        }

        if (body is not null)
            sb.Append($"Content-Length: {body.Length}\r\n");

        sb.Append("\r\n");

        // Send headers.
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(headerBytes, ct);

        // Send body.
        if (body is not null && body.Length > 0)
            await stream.WriteAsync(body, ct);

        await stream.FlushAsync(ct);

        // Read response.
        return await ReadResponseAsync(stream, ct);
    }

    private static async Task<HttpResponseMessage> ReadResponseAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        // Status line.
        var statusLine = await reader.ReadLineAsync(ct) ?? "";
        var parts = statusLine.Split(' ', 3);
        var statusCode = parts.Length >= 2 ? int.Parse(parts[1]) : 200;

        var response = new HttpResponseMessage((HttpStatusCode)statusCode);

        // Headers.
        var contentLength = -1L;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && line.Length > 0)
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;
            var name = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = long.Parse(value);

            response.Headers.TryAddWithoutValidation(name, value);
        }

        // Body.
        if (contentLength > 0)
        {
            var buf = new char[contentLength];
            var read = await reader.ReadBlockAsync(buf, 0, (int)contentLength);
            response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(buf, 0, read));
        }
        else if (contentLength != 0)
        {
            // Read until connection close.
            var remaining = await reader.ReadToEndAsync(ct);
            if (remaining.Length > 0)
                response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(remaining));
        }

        return response;
    }
}
