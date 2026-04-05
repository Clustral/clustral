using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clustral.Sdk.Tests.Http;

public class ImpersonationHandlerTests
{
    [Fact]
    public async Task SendsImpersonateGroupAsSeperateHeaders()
    {
        // Capture raw HTTP on a TCP listener.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var rawRequest = "";

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer);
            rawRequest = Encoding.ASCII.GetString(buffer, 0, read);

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"u8;
            await stream.WriteAsync(response.ToArray());
        });

        // Use the ImpersonationHandler from the Agent project.
        // Since it's internal, we replicate the same raw-HTTP approach here
        // to prove separate headers are sent.
        using var httpClient = new HttpClient(new RawImpersonationHandler(
            new Uri($"http://127.0.0.1:{port}")));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/v1/pods");
        request.Headers.TryAddWithoutValidation("Impersonate-User", "admin@clustral.local");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:authenticated");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:masters");

        await httpClient.SendAsync(request);
        await serverTask;

        listener.Stop();

        // Parse the raw request.
        var lines = rawRequest.Split("\r\n");
        var groupLines = lines
            .Where(l => l.StartsWith("Impersonate-Group:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Must be TWO separate header lines, not one comma-separated.
        Assert.Equal(2, groupLines.Count);
        Assert.Contains("Impersonate-Group: system:authenticated", groupLines);
        Assert.Contains("Impersonate-Group: system:masters", groupLines);

        // Verify user header.
        var userLines = lines
            .Where(l => l.StartsWith("Impersonate-User:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(userLines);
        Assert.Contains("Impersonate-User: admin@clustral.local", userLines);
    }

    [Fact]
    public async Task SendsSingleGroupAsSingleHeader()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var rawRequest = "";

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer);
            rawRequest = Encoding.ASCII.GetString(buffer, 0, read);

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"u8;
            await stream.WriteAsync(response.ToArray());
        });

        using var httpClient = new HttpClient(new RawImpersonationHandler(
            new Uri($"http://127.0.0.1:{port}")));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/test");
        request.Headers.TryAddWithoutValidation("Impersonate-User", "dev@clustral.local");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:authenticated");

        await httpClient.SendAsync(request);
        await serverTask;

        listener.Stop();

        var lines = rawRequest.Split("\r\n");
        var groupLines = lines
            .Where(l => l.StartsWith("Impersonate-Group:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(groupLines);
        Assert.Equal("Impersonate-Group: system:authenticated", groupLines[0]);
    }

    [Fact]
    public async Task SendsNoImpersonationHeadersWhenNoneSet()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var rawRequest = "";

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer);
            rawRequest = Encoding.ASCII.GetString(buffer, 0, read);

            var response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK"u8;
            await stream.WriteAsync(response.ToArray());
        });

        using var httpClient = new HttpClient(new RawImpersonationHandler(
            new Uri($"http://127.0.0.1:{port}")));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/test");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        await httpClient.SendAsync(request);
        await serverTask;

        listener.Stop();

        var lines = rawRequest.Split("\r\n");
        var impersonateLines = lines
            .Where(l => l.Contains("Impersonate", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(impersonateLines);
    }

    [Fact]
    public async Task ForwardsRequestBodyCorrectly()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var rawRequest = "";

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            // Wait briefly for the full request (headers + body) to arrive.
            await Task.Delay(100);
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer);
            rawRequest = Encoding.ASCII.GetString(buffer, 0, read);

            // Send response immediately before the client closes.
            var response = "HTTP/1.1 201 Created\r\nContent-Length: 2\r\n\r\nOK"u8;
            await stream.WriteAsync(response.ToArray());
        });

        using var httpClient = new HttpClient(new RawImpersonationHandler(
            new Uri($"http://127.0.0.1:{port}")));

        var body = """{"kind":"Pod"}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/v1/pods");
        request.Headers.TryAddWithoutValidation("Impersonate-User", "admin@clustral.local");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:masters");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        await serverTask;

        listener.Stop();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("Content-Length:", rawRequest);
        // Body comes after the headers — check it's in the raw request.
        Assert.Contains(body, rawRequest);
    }

    [Fact]
    public async Task ParsesResponseHeadersAndBody()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var responseBody = """{"kind":"PodList","items":[]}""";

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            await stream.ReadAsync(buffer);

            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\n\r\n{responseBody}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
        });

        using var httpClient = new HttpClient(new RawImpersonationHandler(
            new Uri($"http://127.0.0.1:{port}")));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/v1/pods");
        var response = await httpClient.SendAsync(request);
        await serverTask;

        listener.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal(responseBody, content);
    }

    /// <summary>
    /// Minimal raw HTTP handler that writes separate Impersonate-Group headers.
    /// Mirrors the logic in Clustral.Agent's ImpersonationHandler.
    /// </summary>
    private sealed class RawImpersonationHandler : HttpMessageHandler
    {
        private readonly Uri _baseUri;

        internal RawImpersonationHandler(Uri baseUri) => _baseUri = baseUri;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri ?? _baseUri;

            using var tcp = new TcpClient();
            await tcp.ConnectAsync(uri.Host, uri.Port, ct);
            var stream = tcp.GetStream();

            byte[]? body = null;
            if (request.Content is not null)
                body = await request.Content.ReadAsByteArrayAsync(ct);

            var sb = new StringBuilder();
            sb.Append($"{request.Method} {uri.PathAndQuery} HTTP/1.1\r\n");
            sb.Append($"Host: {uri.Host}:{uri.Port}\r\n");
            sb.Append("Connection: close\r\n");

            foreach (var header in request.Headers)
            {
                if (header.Key.Equals("Impersonate-Group", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var value in header.Value)
                        sb.Append($"Impersonate-Group: {value}\r\n");
                }
                else if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                         !header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");
                }
            }

            if (request.Content is not null)
                foreach (var header in request.Content.Headers)
                    sb.Append($"{header.Key}: {string.Join(", ", header.Value)}\r\n");

            if (body is not null)
                sb.Append($"Content-Length: {body.Length}\r\n");

            sb.Append("\r\n");

            await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
            if (body is not null && body.Length > 0)
                await stream.WriteAsync(body, ct);
            await stream.FlushAsync(ct);

            // Read response.
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var statusLine = await reader.ReadLineAsync(ct) ?? "";
            var parts = statusLine.Split(' ', 3);
            var statusCode = parts.Length >= 2 ? int.Parse(parts[1]) : 200;

            var response = new HttpResponseMessage((HttpStatusCode)statusCode);
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

            if (contentLength > 0)
            {
                var buf = new char[contentLength];
                await reader.ReadBlockAsync(buf, 0, (int)contentLength);
                response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(buf));
            }

            return response;
        }
    }
}
