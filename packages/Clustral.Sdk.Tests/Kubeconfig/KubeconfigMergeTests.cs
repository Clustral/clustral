using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Categories 15, 16: merge behavior and input order independence.
/// </summary>
public sealed class KubeconfigMergeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly KubeconfigWriter _sut;

    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public KubeconfigMergeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-merge-{Guid.NewGuid():N}");
        _kubeconfigPath = Path.Combine(_tempDir, "config");
        _sut = new KubeconfigWriter(_kubeconfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Replace strategy (default) ──────────────────────────────────────────

    [Fact]
    public void Merge_Replace_OverwritesExistingEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "old-token"));
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "new-token"), MergeStrategy.Replace);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("new-token", yaml);
        Assert.DoesNotContain("old-token", yaml);
    }

    // ── SkipExisting strategy ───────────────────────────────────────────────

    [Fact]
    public void Merge_SkipExisting_KeepsOriginalEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "original"));
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "new"), MergeStrategy.SkipExisting);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("original", yaml);
        Assert.DoesNotContain("new", yaml);
    }

    [Fact]
    public void Merge_SkipExisting_WritesNewEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "prod-token"));
        _sut.WriteClusterEntry(MakeEntry("clustral-staging", token: "staging-token"), MergeStrategy.SkipExisting);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("prod-token", yaml);
        Assert.Contains("staging-token", yaml);
    }

    // ── FailOnConflict strategy ─────────────────────────────────────────────

    [Fact]
    public void Merge_FailOnConflict_ThrowsOnDuplicate()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "new"), MergeStrategy.FailOnConflict));

        Assert.Contains("clustral-prod", ex.Message);
        Assert.Contains("FailOnConflict", ex.Message);
    }

    [Fact]
    public void Merge_FailOnConflict_SucceedsForNewEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));
        _sut.WriteClusterEntry(MakeEntry("clustral-staging"), MergeStrategy.FailOnConflict);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("clustral-staging", yaml);
    }

    // ── Merge from multiple sources ─────────────────────────────────────────

    [Fact]
    public void Merge_MultipleNonConflicting_AllPresent()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-dev", token: "dev-tok"), setCurrentContext: false);
        _sut.WriteClusterEntry(MakeEntry("clustral-uat", token: "uat-tok"), setCurrentContext: false);
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "prod-tok"), setCurrentContext: true);

        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("dev-tok", yaml);
        Assert.Contains("uat-tok", yaml);
        Assert.Contains("prod-tok", yaml);
        Assert.Contains("current-context: clustral-prod", yaml);

        // Validate references.
        var result = _sut.Validate();
        Assert.True(result.IsValid);
    }

    // ── Current-context preservation ────────────────────────────────────────

    [Fact]
    public void Merge_SkipExisting_PreservesCurrentContext()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-dev"), setCurrentContext: true);
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"), MergeStrategy.SkipExisting, setCurrentContext: true);

        var yaml = File.ReadAllText(_kubeconfigPath);
        // clustral-prod is new, so it should set current-context.
        Assert.Contains("current-context: clustral-prod", yaml);
    }

    // ── Category 16: Input order independence ───────────────────────────────

    [Fact]
    public void Merge_DifferentOrder_SameResult()
    {
        // Order 1: dev → uat → prod.
        _sut.WriteClusterEntry(MakeEntry("clustral-dev", token: "dev"), setCurrentContext: false);
        _sut.WriteClusterEntry(MakeEntry("clustral-uat", token: "uat"), setCurrentContext: false);
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "prod"), setCurrentContext: false);
        var output1 = File.ReadAllText(_kubeconfigPath);

        // Order 2: prod → dev → uat.
        File.Delete(_kubeconfigPath);
        var sut2 = new KubeconfigWriter(_kubeconfigPath);
        sut2.WriteClusterEntry(MakeEntry("clustral-prod", token: "prod"), setCurrentContext: false);
        sut2.WriteClusterEntry(MakeEntry("clustral-dev", token: "dev"), setCurrentContext: false);
        sut2.WriteClusterEntry(MakeEntry("clustral-uat", token: "uat"), setCurrentContext: false);
        var output2 = File.ReadAllText(_kubeconfigPath);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public void Merge_MergeResultsRemainReferentiallyValid()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-a"));
        _sut.WriteClusterEntry(MakeEntry("clustral-b"));
        _sut.WriteClusterEntry(MakeEntry("clustral-c"));

        var result = _sut.Validate();
        Assert.True(result.IsValid);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClustralKubeconfigEntry MakeEntry(
        string name = "clustral-test",
        string server = "https://cp.example.com/proxy/test",
        string token = "test-jwt") =>
        new(name, server, token, AnyExpiry);
}
