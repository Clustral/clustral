using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 6: YAML formatting style — 2-space indentation, no tabs,
/// block-style lists, blank lines between top-level sections.
/// </summary>
public sealed class KubeconfigFormattingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly KubeconfigWriter _sut;

    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public KubeconfigFormattingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-fmt-{Guid.NewGuid():N}");
        _kubeconfigPath = Path.Combine(_tempDir, "config");
        _sut = new KubeconfigWriter(_kubeconfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Formatting_Uses2SpaceIndentation()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);

        // Check that indented lines use 2-space multiples.
        var lines = yaml.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var leading = line.Length - line.TrimStart().Length;
            if (leading > 0)
            {
                Assert.True(leading % 2 == 0,
                    $"Line has {leading}-space indent (not a multiple of 2): '{line}'");
            }
        }
    }

    [Fact]
    public void Formatting_ContainsNoTabs()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.DoesNotContain("\t", yaml);
    }

    [Fact]
    public void Formatting_ListsAreBlockStyle()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);

        // Block-style lists use "- name:" pattern, not flow-style "[{name: ...}]".
        Assert.Contains("- name:", yaml);
        Assert.DoesNotMatch(@"\[.*name:.*\]", yaml);
    }

    [Fact]
    public void Formatting_BlankLinesBetweenTopLevelSections()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);

        // There should be a blank line before clusters, users, contexts, current-context.
        Assert.Contains("\n\nclusters:", yaml);
        Assert.Contains("\n\nusers:", yaml);
        Assert.Contains("\n\ncontexts:", yaml);
        Assert.Contains("\n\ncurrent-context:", yaml);
    }

    [Fact]
    public void Formatting_EndsWithTrailingNewline()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.EndsWith("\n", yaml);
    }

    [Fact]
    public void Formatting_IsUtf8Safe()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var bytes = File.ReadAllBytes(_kubeconfigPath);

        // Verify the file is valid UTF-8 by round-tripping.
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var reencoded = System.Text.Encoding.UTF8.GetBytes(text);

        Assert.Equal(bytes.Length, reencoded.Length);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClustralKubeconfigEntry MakeEntry(
        string name = "clustral-test",
        string server = "https://cp.example.com/proxy/test",
        string token = "test-jwt") =>
        new(name, server, token, AnyExpiry);
}
