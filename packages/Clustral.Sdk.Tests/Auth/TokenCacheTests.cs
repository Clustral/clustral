using Clustral.Sdk.Auth;

namespace Clustral.Sdk.Tests.Auth;

/// <summary>
/// Each test gets its own temp directory so tests are fully isolated and
/// nothing is written to the real ~/.clustral/token.
/// </summary>
public sealed class TokenCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tokenPath;
    private readonly TokenCache _sut;

    public TokenCacheTests()
    {
        _tempDir   = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _tokenPath = Path.Combine(_tempDir, "token");
        _sut       = new TokenCache(_tokenPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // StoreAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_CreatesDirectoryAndFile()
    {
        await _sut.StoreAsync("tok1");

        Assert.True(File.Exists(_tokenPath));
    }

    [Fact]
    public async Task StoreAsync_WritesTokenToFile()
    {
        await _sut.StoreAsync("my-jwt-token");

        var contents = await File.ReadAllTextAsync(_tokenPath);
        Assert.Equal("my-jwt-token", contents);
    }

    [Fact]
    public async Task StoreAsync_OverwritesPreviousToken()
    {
        await _sut.StoreAsync("first");
        await _sut.StoreAsync("second");

        var contents = await File.ReadAllTextAsync(_tokenPath);
        Assert.Equal("second", contents);
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnNullToken()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _sut.StoreAsync(null!));
    }

    [Fact]
    public async Task StoreAsync_ThrowsOnEmptyToken()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _sut.StoreAsync(string.Empty));
    }

    // -------------------------------------------------------------------------
    // ReadAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReadAsync_ReturnsNullWhenFileDoesNotExist()
    {
        var result = await _sut.ReadAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_ReturnsStoredToken()
    {
        await _sut.StoreAsync("stored-token");

        var result = await _sut.ReadAsync();

        Assert.Equal("stored-token", result);
    }

    [Fact]
    public async Task ReadAsync_TrimsWhitespace()
    {
        // Simulate a file that was written with a trailing newline by another tool.
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_tokenPath, "  tok-with-space  \n");

        var result = await _sut.ReadAsync();

        Assert.Equal("tok-with-space", result);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullForWhitespaceOnlyFile()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(_tokenPath, "   \n  ");

        var result = await _sut.ReadAsync();

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // ClearAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_DeletesTokenFile()
    {
        await _sut.StoreAsync("to-be-deleted");

        await _sut.ClearAsync();

        Assert.False(File.Exists(_tokenPath));
    }

    [Fact]
    public async Task ClearAsync_IsIdempotentWhenFileDoesNotExist()
    {
        // Should not throw.
        await _sut.ClearAsync();
        await _sut.ClearAsync();
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullAfterClear()
    {
        await _sut.StoreAsync("temporary");
        await _sut.ClearAsync();

        var result = await _sut.ReadAsync();

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // Concurrent access
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StoreAsync_ConcurrentWritesDoNotCorruptFile()
    {
        // Fire several concurrent stores; the last one wins but none should throw.
        var tasks = Enumerable
            .Range(0, 20)
            .Select(i => _sut.StoreAsync($"token-{i:D3}"));

        await Task.WhenAll(tasks);

        var result = await _sut.ReadAsync();
        Assert.NotNull(result);
        Assert.StartsWith("token-", result);
    }

    [Fact]
    public async Task ReadAsync_ConcurrentReadsAllSucceed()
    {
        await _sut.StoreAsync("shared-token");

        var tasks = Enumerable
            .Range(0, 20)
            .Select(_ => _sut.ReadAsync());

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("shared-token", r));
    }
}
