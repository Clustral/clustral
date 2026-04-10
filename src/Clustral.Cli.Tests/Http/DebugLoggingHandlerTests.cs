using System.Net;
using Clustral.Cli.Http;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Http;

/// <summary>
/// Tests for <see cref="DebugLoggingHandler"/> — the HTTP pipeline handler
/// that traces requests/responses when <see cref="CliDebug.Enabled"/> is true.
/// </summary>
public sealed class DebugLoggingHandlerTests(ITestOutputHelper output) : IDisposable
{
    private readonly bool _origDebug = CliDebug.Enabled;

    public void Dispose() => CliDebug.Enabled = _origDebug;

    [Fact]
    public async Task DebugDisabled_NoLogging_RequestPassesThrough()
    {
        CliDebug.Enabled = false;
        var inner = new FakeHandler(HttpStatusCode.OK);
        using var handler = new DebugLoggingHandler(inner);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var response = await http.GetAsync("test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task DebugEnabled_RequestPassesThrough()
    {
        CliDebug.Enabled = true;
        var inner = new FakeHandler(HttpStatusCode.NotFound);
        using var handler = new DebugLoggingHandler(inner);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var response = await http.GetAsync("api/v1/clusters");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task DebugEnabled_CapturesRequestMethod()
    {
        CliDebug.Enabled = true;
        var inner = new FakeHandler(HttpStatusCode.OK);
        using var handler = new DebugLoggingHandler(inner);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        await http.GetAsync("api/v1/clusters");

        inner.LastRequest.Should().NotBeNull();
        inner.LastRequest!.Method.Should().Be(HttpMethod.Get);

        output.WriteLine($"Request: {inner.LastRequest.Method} {inner.LastRequest.RequestUri}");
    }

    /// <summary>
    /// Minimal handler that records call count + last request and returns
    /// a canned status code. No real HTTP.
    /// </summary>
    private sealed class FakeHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
