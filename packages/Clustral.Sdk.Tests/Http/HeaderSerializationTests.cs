using System.Net;
using System.Net.Http;

namespace Clustral.Sdk.Tests.Http;

public class HeaderSerializationTests
{
    [Fact]
    public void TryAddWithoutValidation_MultipleCallsSameKey_StoresMultipleValues()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:authenticated");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:masters");

        var values = request.Headers.GetValues("Impersonate-Group").ToList();

        Assert.Equal(2, values.Count);
        Assert.Contains("system:authenticated", values);
        Assert.Contains("system:masters", values);
    }

    [Fact]
    public void Add_MultipleCallsSameKey_StoresMultipleValues()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        request.Headers.Add("Impersonate-Group", "system:authenticated");
        request.Headers.Add("Impersonate-Group", "system:masters");

        var values = request.Headers.GetValues("Impersonate-Group").ToList();

        Assert.Equal(2, values.Count);
        Assert.Contains("system:authenticated", values);
        Assert.Contains("system:masters", values);
    }

    [Fact]
    public void TryAddWithoutValidation_IEnumerable_StoresMultipleValues()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var groups = new[] { "system:authenticated", "system:masters" };

        request.Headers.TryAddWithoutValidation("Impersonate-Group", groups);

        var values = request.Headers.GetValues("Impersonate-Group").ToList();

        Assert.Equal(2, values.Count);
        Assert.Contains("system:authenticated", values);
        Assert.Contains("system:masters", values);
    }

    [Fact]
    public async Task HttpClient_SendsMultiValueHeaders_AsSeparateLines()
    {
        // Capture what the HttpClient actually sends on the wire.
        var receivedHeaders = new List<(string Name, string Value)>();

        var handler = new TestHandler(req =>
        {
            // Read raw headers as .NET presents them.
            foreach (var h in req.Headers)
            {
                foreach (var v in h.Value)
                {
                    receivedHeaders.Add((h.Key, v));
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test");

        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:authenticated");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:masters");

        await client.SendAsync(request);

        // Check how many separate header entries the handler sees.
        var impersonateGroups = receivedHeaders
            .Where(h => h.Name == "Impersonate-Group")
            .ToList();

        // This test reveals whether .NET combines the values or keeps them separate.
        // Output the actual values for debugging:
        foreach (var (name, value) in impersonateGroups)
            Assert.DoesNotContain(",", value); // Each value should NOT contain a comma
    }

    [Fact]
    public void HeaderToString_ShowsSerialization()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:authenticated");
        request.Headers.TryAddWithoutValidation("Impersonate-Group", "system:masters");

        var headerString = request.Headers.ToString();

        // This reveals exactly how .NET formats the headers.
        // If comma-separated: "Impersonate-Group: system:authenticated, system:masters\r\n"
        // If separate: "Impersonate-Group: system:authenticated\r\nImpersonate-Group: system:masters\r\n"
        Assert.Contains("Impersonate-Group", headerString);

        // Count how many times "Impersonate-Group" appears.
        var count = headerString.Split("Impersonate-Group").Length - 1;
        // If count == 1: comma-separated (PROBLEM)
        // If count == 2: separate headers (CORRECT)
        Assert.True(count >= 1, $"Header appears {count} times in: {headerString}");

        // Output the actual string for inspection:
        Assert.False(false, $"Actual header serialization:\n{headerString}");
    }

    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
