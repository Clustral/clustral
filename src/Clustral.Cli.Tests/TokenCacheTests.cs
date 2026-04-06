using Clustral.Sdk.Auth;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests;

public class TokenCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tokenPath;
    private readonly ITestOutputHelper _output;

    public TokenCacheTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"clustral-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tokenPath = Path.Combine(_tempDir, "token");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        var cache = new TokenCache(_tokenPath);
        var result = await cache.ReadAsync();

        _output.WriteLine($"Path:   {_tokenPath}");
        _output.WriteLine($"Exists: {File.Exists(_tokenPath)}");
        _output.WriteLine($"Result: {result ?? "(null)"}");

        Assert.Null(result);
    }

    [Fact]
    public async Task StoreAsync_ThenReadAsync_RoundTrips()
    {
        var cache = new TokenCache(_tokenPath);
        await cache.StoreAsync("my-test-token");

        var result = await cache.ReadAsync();

        _output.WriteLine($"Stored:  \"my-test-token\"");
        _output.WriteLine($"Read:    \"{result}\"");

        Assert.Equal("my-test-token", result);
    }

    [Fact]
    public async Task StoreAsync_OverwritesPreviousToken()
    {
        var cache = new TokenCache(_tokenPath);
        await cache.StoreAsync("token-1");
        await cache.StoreAsync("token-2");

        var result = await cache.ReadAsync();

        _output.WriteLine($"Wrote:  \"token-1\" then \"token-2\"");
        _output.WriteLine($"Read:   \"{result}\"");

        Assert.Equal("token-2", result);
    }

    [Fact]
    public async Task ClearAsync_DeletesToken()
    {
        var cache = new TokenCache(_tokenPath);
        await cache.StoreAsync("token-to-delete");
        await cache.ClearAsync();

        var result = await cache.ReadAsync();

        _output.WriteLine($"Stored, then cleared");
        _output.WriteLine($"File exists: {File.Exists(_tokenPath)}");
        _output.WriteLine($"Read result: {result ?? "(null)"}");

        Assert.Null(result);
        Assert.False(File.Exists(_tokenPath));
    }

    [Fact]
    public async Task ClearAsync_NoOpWhenFileDoesNotExist()
    {
        var cache = new TokenCache(_tokenPath);

        _output.WriteLine($"Clearing non-existent file at {_tokenPath}");
        _output.WriteLine("Expected: no exception");

        await cache.ClearAsync();
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenFileIsWhitespace()
    {
        await File.WriteAllTextAsync(_tokenPath, "   \n  \t  ");
        var cache = new TokenCache(_tokenPath);
        var result = await cache.ReadAsync();

        _output.WriteLine("File content: \"   \\n  \\t  \" (whitespace only)");
        _output.WriteLine($"Result: {result ?? "(null)"}");

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAsync_TrimsWhitespace()
    {
        await File.WriteAllTextAsync(_tokenPath, "  my-token  \n");
        var cache = new TokenCache(_tokenPath);
        var result = await cache.ReadAsync();

        _output.WriteLine("File content: \"  my-token  \\n\"");
        _output.WriteLine($"Trimmed read: \"{result}\"");

        Assert.Equal("my-token", result);
    }

    [Fact]
    public async Task StoreAsync_CreatesParentDirectory()
    {
        var nested = Path.Combine(_tempDir, "sub", "dir", "token");
        var cache = new TokenCache(nested);
        await cache.StoreAsync("nested-token");

        var result = await cache.ReadAsync();

        _output.WriteLine($"Nested path: {nested}");
        _output.WriteLine($"Directories created: {Directory.Exists(Path.GetDirectoryName(nested)!)}");
        _output.WriteLine($"Token read:  \"{result}\"");

        Assert.Equal("nested-token", result);
    }

    [Fact]
    public void Constructor_ThrowsOnNullOrEmpty()
    {
        _output.WriteLine("TokenCache(\"\") => ArgumentException");
        _output.WriteLine("TokenCache(null) => ArgumentNullException");

        Assert.Throws<ArgumentException>(() => new TokenCache(""));
        Assert.Throws<ArgumentNullException>(() => new TokenCache(null!));
    }
}
