using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Clustral.Agent.Proxy;

/// <summary>
/// Workaround for .NET HttpClient combining multi-value headers into a
/// single comma-separated line. k8s requires each Impersonate-Group as
/// a separate HTTP header line.
///
/// This handler uses SocketsHttpHandler with a custom ConnectCallback
/// that wraps the network stream with an interceptor. The interceptor
/// rewrites the outgoing HTTP headers to split comma-separated
/// Impersonate-Group values into separate header lines before they
/// reach the wire.
/// </summary>
internal sealed class ImpersonationHandler : DelegatingHandler
{
    internal ImpersonationHandler(HttpMessageHandler inner) : base(inner) { }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // The actual header splitting happens in ImpersonationStream
        // which is injected via SocketsHttpHandler.ConnectCallback.
        return base.SendAsync(request, ct);
    }

    /// <summary>
    /// Creates a SocketsHttpHandler with a ConnectCallback that wraps the
    /// stream with an ImpersonationStream to fix header serialization.
    /// </summary>
    internal static SocketsHttpHandler CreateHandler(bool skipTlsVerify)
    {
        const string caCertPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

        var handler = new SocketsHttpHandler();

        if (skipTlsVerify)
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
        }

        // Load the in-cluster CA cert for k8s API server TLS verification.
        X509Certificate2? caCert = null;
        if (!skipTlsVerify && File.Exists(caCertPath))
            caCert = X509CertificateLoader.LoadCertificateFromFile(caCertPath);

        handler.ConnectCallback = async (context, ct) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(context.DnsEndPoint, ct);
            var networkStream = new NetworkStream(socket, ownsSocket: true);

            Stream stream = networkStream;

            // For HTTPS, wrap with SslStream.
            if (context.DnsEndPoint.Port == 443 ||
                context.InitialRequestMessage.RequestUri?.Scheme == "https")
            {
                RemoteCertificateValidationCallback? certCallback = null;

                if (skipTlsVerify)
                {
                    certCallback = (_, _, _, _) => true;
                }
                else if (caCert is not null)
                {
                    // Validate using the in-cluster CA cert.
                    certCallback = (_, cert, _, _) =>
                    {
                        if (cert is null) return false;
                        using var chain = new X509Chain();
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(caCert);
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        return chain.Build(new X509Certificate2(cert));
                    };
                }

                var sslStream = new SslStream(stream, false, certCallback);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = context.DnsEndPoint.Host,
                    RemoteCertificateValidationCallback = certCallback,
                }, ct);
                stream = sslStream;
            }

            return new ImpersonationStream(stream);
        };

        return handler;
    }
}

/// <summary>
/// Stream wrapper that intercepts HTTP header writes and splits
/// comma-separated Impersonate-Group values into separate header lines.
/// </summary>
internal sealed class ImpersonationStream : Stream
{
    private readonly Stream _inner;
    private bool _headersDone;

    internal ImpersonationStream(Stream inner) => _inner = inner;

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        if (_headersDone)
        {
            await _inner.WriteAsync(buffer, ct);
            return;
        }

        // Check if this write contains the end-of-headers marker.
        var data = Encoding.ASCII.GetString(buffer.Span);

        var headerEndIdx = data.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEndIdx < 0)
        {
            // Still in headers — buffer and rewrite.
            var rewritten = RewriteHeaders(data);
            await _inner.WriteAsync(Encoding.ASCII.GetBytes(rewritten), ct);
            return;
        }

        // Split headers from body.
        _headersDone = true;
        var headers = data[..headerEndIdx];
        var rest = data[(headerEndIdx)..]; // includes \r\n\r\n + body

        var rewrittenHeaders = RewriteHeaders(headers);
        await _inner.WriteAsync(Encoding.ASCII.GetBytes(rewrittenHeaders + rest), ct);
    }

    private static string RewriteHeaders(string headers)
    {
        var lines = headers.Split("\r\n");
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("Impersonate-Group:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Impersonate-Group:".Length..].Trim();
                // Split comma-separated values into separate headers.
                var groups = value.Split(',', StringSplitOptions.TrimEntries);
                foreach (var group in groups)
                    result.Append($"Impersonate-Group: {group}\r\n");
            }
            else
            {
                result.Append(line).Append("\r\n");
            }
        }

        // Remove trailing extra \r\n (the loop adds one per line including the last).
        var s = result.ToString();
        if (s.EndsWith("\r\n\r\n"))
            s = s[..^2]; // keep only one trailing \r\n
        return s;
    }

    // Delegate everything else to the inner stream.
    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        _inner.ReadAsync(buffer, ct);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        _inner.ReadAsync(buffer, offset, count, ct);

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, count);

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
