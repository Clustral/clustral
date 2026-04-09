using Clustral.Cli.Http;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Http;

public sealed class CliHttpTests(ITestOutputHelper output)
{
    [Fact]
    public void CreateClient_AppliesDefaultTimeout()
    {
        using var http = CliHttp.CreateClient("https://example.com");

        http.Timeout.Should().Be(CliHttp.DefaultTimeout);
        http.BaseAddress!.ToString().Should().Be("https://example.com/");
    }

    [Fact]
    public void CreateClient_RespectsCustomTimeout()
    {
        var custom = TimeSpan.FromSeconds(15);
        using var http = CliHttp.CreateClient("https://example.com", timeout: custom);

        http.Timeout.Should().Be(custom);
    }

    [Fact]
    public void CreateClient_TrimsTrailingSlashAndNormalizes()
    {
        using var http = CliHttp.CreateClient("https://example.com/");

        // BaseAddress should always end with a single trailing slash.
        http.BaseAddress!.ToString().Should().Be("https://example.com/");
    }

    [Fact]
    public void CreateClient_NullOrEmptyBaseUrl_Throws()
    {
        Action act = () => CliHttp.CreateClient("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RunWithSpinnerAsync_ReturnsResultOnSuccess()
    {
        var result = await CliHttp.RunWithSpinnerAsync(
            "test work...",
            _ => Task.FromResult(42));

        result.Should().Be(42);
    }

    [Fact]
    public async Task RunWithSpinnerAsync_ConvertsTaskCanceledToCliHttpTimeout()
    {
        var act = async () => await CliHttp.RunWithSpinnerAsync<int>(
            "test work...",
            _ => throw new TaskCanceledException("simulated timeout"));

        var ex = await act.Should().ThrowAsync<CliHttpTimeoutException>();
        ex.Which.StatusMessage.Should().Be("test work...");
        output.WriteLine(ex.Which.Message);
    }

    [Fact]
    public async Task RunWithSpinnerAsync_ConvertsOperationCanceledToCliHttpTimeout()
    {
        var act = async () => await CliHttp.RunWithSpinnerAsync<int>(
            "test work...",
            _ => throw new OperationCanceledException("simulated"));

        await act.Should().ThrowAsync<CliHttpTimeoutException>();
    }

    [Fact]
    public async Task RunWithSpinnerAsync_PropagatesNonTimeoutErrors()
    {
        var act = async () => await CliHttp.RunWithSpinnerAsync<int>(
            "test work...",
            _ => throw new InvalidOperationException("real error"));

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be("real error");
    }

    [Fact]
    public async Task RunWithSpinnerAsync_NonGenericOverload_Works()
    {
        var executed = false;
        await CliHttp.RunWithSpinnerAsync(
            "test work...",
            _ =>
            {
                executed = true;
                return Task.CompletedTask;
            });

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task RunWithSpinnerAsync_ActuallyTimesOut_WhenHttpClientTimeoutHit()
    {
        // This test exercises the real HttpClient timeout path.
        // Use a non-routable IP so the connection hangs, with a 1s timeout.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var act = async () => await CliHttp.RunWithSpinnerAsync<string>(
            "Connecting to unreachable host...",
            ct => http.GetStringAsync("http://10.255.255.1:81/", ct));

        await act.Should().ThrowAsync<CliHttpTimeoutException>();
        sw.Stop();

        // Should fail in roughly 1 second, definitely not hang for 100 seconds.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        output.WriteLine($"Timed out in {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void IsTimeout_DetectsTaskCanceledException()
    {
        CliHttp.IsTimeout(new TaskCanceledException()).Should().BeTrue();
    }

    [Fact]
    public void IsTimeout_DetectsOperationCanceledException()
    {
        CliHttp.IsTimeout(new OperationCanceledException()).Should().BeTrue();
    }

    [Fact]
    public void IsTimeout_DetectsInnerTaskCanceled()
    {
        var wrapped = new HttpRequestException("network error", new TaskCanceledException());
        CliHttp.IsTimeout(wrapped).Should().BeTrue();
    }

    [Fact]
    public void IsTimeout_FalseForRegularException()
    {
        CliHttp.IsTimeout(new InvalidOperationException()).Should().BeFalse();
    }
}
