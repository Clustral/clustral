using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clustral.Sdk.Tests.Http;

public class WireFormatTests
{
    [Fact]
    public async Task HttpClient_MultiValueHeader_WireFormat()
    {
        // Start a TCP listener to capture the raw HTTP request.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var rawRequest = "";

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            rawRequest = Encoding.ASCII.GetString(buffer, 0, read);

            // Send minimal HTTP response.
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8;
            await stream.WriteAsync(response.ToArray());
        });

        // Send request with multiple Impersonate-Group values.
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/test");

        request.Headers.TryAddWithoutValidation("Impersonate-User", "admin@clustral.local");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:authenticated");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:masters");

        try { await httpClient.SendAsync(request); } catch { /* connection might close */ }

        await serverTask;

        listener.Stop();

        // Analyze the raw wire format.
        var lines = rawRequest.Split("\r\n");
        var groupLines = lines.Where(l => l.StartsWith("Impersonate-Group:", StringComparison.OrdinalIgnoreCase)).ToList();

        // Output for debugging.
        var impersonateLines = lines.Where(l => l.Contains("Impersonate", StringComparison.OrdinalIgnoreCase)).ToList();

        // THE KEY ASSERTION:
        // If .NET sends them as separate headers:
        //   Impersonate-Group: system:authenticated
        //   Impersonate-Group: system:masters
        //   → groupLines.Count == 2
        //
        // If .NET combines them:
        //   Impersonate-Group: system:authenticated, system:masters
        //   → groupLines.Count == 1

        Assert.True(groupLines.Count > 0,
            $"No Impersonate-Group headers found. Raw request:\n{rawRequest}");

        // .NET HttpClient combines multi-value headers into ONE comma-separated
        // line. This is expected and is why the Agent uses ImpersonationHandler
        // (raw HTTP) instead of HttpClient for k8s API requests.
        Assert.Single(groupLines);
        Assert.Contains(",", groupLines[0]); // Confirms comma-separated — the bug we work around
    }
}
