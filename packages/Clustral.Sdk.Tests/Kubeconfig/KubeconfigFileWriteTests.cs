using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 17: file write behavior — atomic writes, overwrite, permissions.
/// </summary>
public sealed class KubeconfigFileWriteTests : IDisposable
{
    private readonly string _tempDir;

    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public KubeconfigFileWriteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Write_CreatesFileWhenNotExists()
    {
        var path = Path.Combine(_tempDir, "new-config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "test", "https://test.example.com", "tok", AnyExpiry));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "config");
        // Write valid YAML so the reader doesn't choke.
        File.WriteAllText(path, """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users: []
            contexts: []
            current-context: ''
            """);

        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "test", "https://test.example.com", "tok", AnyExpiry));

        var content = File.ReadAllText(path);
        Assert.Contains("apiVersion: v1", content);
        Assert.Contains("test", content);
        Assert.Contains("tok", content);
    }

    [Fact]
    public void Write_OutputMatchesSerializedContent()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "test", "https://test.example.com", "tok", AnyExpiry));

        var content = File.ReadAllText(path);
        Assert.Contains("apiVersion: v1", content);
        Assert.Contains("kind: Config", content);
        Assert.Contains("test", content);
    }

    [Fact]
    public void Write_NoTempFileLeftBehind()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "test", "https://test.example.com", "tok", AnyExpiry));

        Assert.False(File.Exists(path + ".tmp"),
            "Temp file should be cleaned up after atomic write");
    }

    [Fact]
    public void Write_CreatesParentDirectories()
    {
        var path = Path.Combine(_tempDir, "deep", "nested", "dir", "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "test", "https://test.example.com", "tok", AnyExpiry));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Write_OnUnix_SetsRestrictivePermissions()
    {
        if (OperatingSystem.IsWindows())
            return; // Skip on Windows.

        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "test", "https://test.example.com", "tok", AnyExpiry));

        var mode = File.GetUnixFileMode(path);
        // Should be owner-only read/write (0600).
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Write_InvalidPath_Throws()
    {
        // A path that cannot be created (null bytes, too long, etc.).
        var sut = new KubeconfigWriter("/\0invalid");

        Assert.ThrowsAny<Exception>(() =>
            sut.WriteClusterEntry(new ClustralKubeconfigEntry(
                "test", "https://test.example.com", "tok", AnyExpiry)));
    }
}
