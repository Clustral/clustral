using Clustral.Sdk.Auth;
using Clustral.Sdk.Grpc;

namespace Clustral.Sdk.Tests.Grpc;

public sealed class GrpcChannelFactoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tokenPath;
    private readonly TokenCache _tokenCache;
    private readonly GrpcChannelFactory _sut;

    public GrpcChannelFactoryTests()
    {
        _tempDir    = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _tokenPath  = Path.Combine(_tempDir, "token");
        _tokenCache = new TokenCache(_tokenPath);
        _sut        = new GrpcChannelFactory(_tokenCache);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // CreateAuthenticatedChannelAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAuthenticatedChannelAsync_ThrowsWhenNoTokenIsStored()
    {
        // No token has been written.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAuthenticatedChannelAsync("https://cp.example.com:5001"));
    }

    [Fact]
    public async Task CreateAuthenticatedChannelAsync_ReturnsChannelWhenTokenExists()
    {
        await _tokenCache.StoreAsync("valid-jwt");

        using var channel = await _sut.CreateAuthenticatedChannelAsync(
            "https://cp.example.com:5001");

        Assert.NotNull(channel);
    }

    [Fact]
    public async Task CreateAuthenticatedChannelAsync_ChannelAddressMatchesInput()
    {
        await _tokenCache.StoreAsync("valid-jwt");

        using var channel = await _sut.CreateAuthenticatedChannelAsync(
            "https://cp.example.com:5001");

        // Grpc.Net.Client normalises the Target by stripping the scheme.
        Assert.Equal("cp.example.com:5001", channel.Target);
    }

    [Fact]
    public async Task CreateAuthenticatedChannelAsync_ThrowsAfterTokenIsCleared()
    {
        await _tokenCache.StoreAsync("will-be-cleared");
        await _tokenCache.ClearAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAuthenticatedChannelAsync("https://cp.example.com:5001"));
    }

    [Fact]
    public async Task CreateAuthenticatedChannelAsync_RespectsCancellation()
    {
        // A token must exist so the fast-fail check passes and execution
        // reaches the SemaphoreSlim.WaitAsync(ct) call, which throws on a
        // pre-cancelled token.
        await _tokenCache.StoreAsync("some-token");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.CreateAuthenticatedChannelAsync(
                "https://cp.example.com:5001", cts.Token));
    }

    // -------------------------------------------------------------------------
    // CreateWithToken (static)
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateWithToken_ReturnsChannel()
    {
        using var channel = GrpcChannelFactory.CreateWithToken(
            "https://cp.example.com:5001", "static-jwt");

        Assert.NotNull(channel);
    }

    [Fact]
    public void CreateWithToken_ChannelAddressMatchesInput()
    {
        using var channel = GrpcChannelFactory.CreateWithToken(
            "https://cp.example.com:5001", "tok");

        // Grpc.Net.Client normalises the Target by stripping the scheme.
        Assert.Equal("cp.example.com:5001", channel.Target);
    }

    [Fact]
    public void CreateWithToken_ThrowsOnEmptyToken()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => GrpcChannelFactory.CreateWithToken("https://cp.example.com:5001", string.Empty));
    }

    [Fact]
    public void CreateWithToken_ThrowsOnNullToken()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => GrpcChannelFactory.CreateWithToken("https://cp.example.com:5001", null!));
    }

    // -------------------------------------------------------------------------
    // CreateInsecureWithToken (static)
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateInsecureWithToken_ReturnsChannel()
    {
        using var channel = GrpcChannelFactory.CreateInsecureWithToken(
            "http://localhost:5001", "local-jwt");

        Assert.NotNull(channel);
    }

    [Fact]
    public void CreateInsecureWithToken_ChannelAddressMatchesInput()
    {
        // Grpc.Net.Client strips the scheme from http:// targets.
        using var channel = GrpcChannelFactory.CreateInsecureWithToken("http://localhost:5001", "tok");

        Assert.Equal("localhost:5001", channel.Target);
    }

    [Fact]
    public void CreateInsecureWithToken_ThrowsOnEmptyToken()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => GrpcChannelFactory.CreateInsecureWithToken("http://localhost:5001", string.Empty));
    }

    // -------------------------------------------------------------------------
    // Channel disposal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAuthenticatedChannelAsync_ChannelCanBeDisposed()
    {
        await _tokenCache.StoreAsync("jwt");
        var channel = await _sut.CreateAuthenticatedChannelAsync("https://cp.example.com:5001");

        // Verify Dispose() does not throw; Grpc.Net.Client does not guarantee
        // the state transitions to Shutdown synchronously.
        var ex = Record.Exception(() => channel.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void CreateWithToken_ChannelCanBeDisposed()
    {
        var channel = GrpcChannelFactory.CreateWithToken("https://cp.example.com:5001", "tok");

        var ex = Record.Exception(() => channel.Dispose());
        Assert.Null(ex);
    }
}
