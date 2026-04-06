using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Categories 3, 4, 5: top-level section ordering, entry ordering, field ordering.
/// </summary>
public sealed class KubeconfigOrderingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly KubeconfigWriter _sut;

    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public KubeconfigOrderingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-order-{Guid.NewGuid():N}");
        _kubeconfigPath = Path.Combine(_tempDir, "config");
        _sut = new KubeconfigWriter(_kubeconfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Category 3: Top-level section ordering ──────────────────────────────

    [Fact]
    public void TopLevelSections_AppearInCanonicalOrder()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);

        var lines = yaml.Split('\n');
        var sectionIndices = new Dictionary<string, int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd();
            foreach (var section in new[] { "apiVersion:", "kind:", "preferences:", "clusters:", "users:", "contexts:", "current-context:" })
            {
                if (trimmed.StartsWith(section))
                    sectionIndices[section.TrimEnd(':')] = i;
            }
        }

        Assert.True(sectionIndices["apiVersion"] < sectionIndices["kind"],
            "apiVersion must come before kind");
        Assert.True(sectionIndices["kind"] < sectionIndices["preferences"],
            "kind must come before preferences");
        Assert.True(sectionIndices["preferences"] < sectionIndices["clusters"],
            "preferences must come before clusters");
        Assert.True(sectionIndices["clusters"] < sectionIndices["users"],
            "clusters must come before users");
        Assert.True(sectionIndices["users"] < sectionIndices["contexts"],
            "users must come before contexts");
        Assert.True(sectionIndices["contexts"] < sectionIndices["current-context"],
            "contexts must come before current-context");
    }

    [Fact]
    public void TopLevelSections_NoUnexpectedReordering_AfterUpsert()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-first"));
        _sut.WriteClusterEntry(MakeEntry("clustral-second"));

        var yaml = File.ReadAllText(_kubeconfigPath);
        var lines = yaml.Split('\n');

        var apiIdx = Array.FindIndex(lines, l => l.StartsWith("apiVersion:"));
        var currentCtxIdx = Array.FindIndex(lines, l => l.StartsWith("current-context:"));

        Assert.True(apiIdx < currentCtxIdx,
            "apiVersion must remain before current-context after upsert");
    }

    // ── Category 4: Entry ordering inside sections ──────────────────────────

    [Fact]
    public void Clusters_AreOrderedAlphabeticallyByName()
    {
        // Insert in reverse order.
        _sut.WriteClusterEntry(MakeEntry("clustral-zebra"));
        _sut.WriteClusterEntry(MakeEntry("clustral-alpha"));
        _sut.WriteClusterEntry(MakeEntry("clustral-mid"));

        var yaml = File.ReadAllText(_kubeconfigPath);

        var alphaIdx = yaml.IndexOf("clustral-alpha", StringComparison.Ordinal);
        var midIdx = yaml.IndexOf("clustral-mid", StringComparison.Ordinal);
        var zebraIdx = yaml.IndexOf("clustral-zebra", StringComparison.Ordinal);

        Assert.True(alphaIdx < midIdx, "alpha must come before mid");
        Assert.True(midIdx < zebraIdx, "mid must come before zebra");
    }

    [Fact]
    public void Users_AreOrderedAlphabeticallyByName()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-zulu"));
        _sut.WriteClusterEntry(MakeEntry("clustral-bravo"));

        var yaml = File.ReadAllText(_kubeconfigPath);

        // Find user section entries.
        var usersStart = yaml.IndexOf("\nusers:", StringComparison.Ordinal);
        var contextsStart = yaml.IndexOf("\ncontexts:", StringComparison.Ordinal);
        var usersSection = yaml[usersStart..contextsStart];

        var bravoIdx = usersSection.IndexOf("clustral-bravo", StringComparison.Ordinal);
        var zuluIdx = usersSection.IndexOf("clustral-zulu", StringComparison.Ordinal);

        Assert.True(bravoIdx < zuluIdx, "bravo must come before zulu in users section");
    }

    [Fact]
    public void Contexts_AreOrderedAlphabeticallyByName()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-zulu"));
        _sut.WriteClusterEntry(MakeEntry("clustral-bravo"));

        var yaml = File.ReadAllText(_kubeconfigPath);

        var contextsStart = yaml.IndexOf("\ncontexts:", StringComparison.Ordinal);
        var currentCtxStart = yaml.IndexOf("\ncurrent-context:", StringComparison.Ordinal);
        var contextsSection = yaml[contextsStart..currentCtxStart];

        var bravoIdx = contextsSection.IndexOf("clustral-bravo", StringComparison.Ordinal);
        var zuluIdx = contextsSection.IndexOf("clustral-zulu", StringComparison.Ordinal);

        Assert.True(bravoIdx < zuluIdx, "bravo must come before zulu in contexts section");
    }

    [Fact]
    public void EntryOrdering_IsStable_AcrossRepeatedWrites()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-c"));
        _sut.WriteClusterEntry(MakeEntry("clustral-a"));
        _sut.WriteClusterEntry(MakeEntry("clustral-b"));
        var output1 = File.ReadAllText(_kubeconfigPath);

        // Update one entry — ordering should remain stable.
        _sut.WriteClusterEntry(MakeEntry("clustral-b", token: "new-token"));
        var output2 = File.ReadAllText(_kubeconfigPath);

        // The only change should be the token value.
        Assert.Equal(
            output1.Replace("test-jwt", "new-token"),
            output2.Replace("test-jwt", "new-token"));
    }

    // ── Category 5: Field ordering within entries ───────────────────────────

    [Fact]
    public void ClusterFields_ServerBeforeInsecureSkipTlsVerify()
    {
        var entry = new ClustralKubeconfigEntry(
            "clustral-dev", "https://dev.example.com", "tok", AnyExpiry,
            InsecureSkipTlsVerify: true);
        _sut.WriteClusterEntry(entry);

        var yaml = File.ReadAllText(_kubeconfigPath);
        var serverIdx = yaml.IndexOf("server:", StringComparison.Ordinal);
        var insecureIdx = yaml.IndexOf("insecure-skip-tls-verify:", StringComparison.Ordinal);

        Assert.True(serverIdx < insecureIdx,
            "server must come before insecure-skip-tls-verify");
    }

    [Fact]
    public void ContextFields_ClusterBeforeUserBeforeNamespace()
    {
        // Write a kubeconfig with a context, then manually add namespace.
        _sut.WriteClusterEntry(MakeEntry("clustral-test"));

        var yaml = File.ReadAllText(_kubeconfigPath);
        var contextsStart = yaml.IndexOf("\ncontexts:", StringComparison.Ordinal);
        var contextSection = yaml[contextsStart..];

        var clusterIdx = contextSection.IndexOf("cluster: clustral-test", StringComparison.Ordinal);
        var userIdx = contextSection.IndexOf("user: clustral-test", StringComparison.Ordinal);

        Assert.True(clusterIdx < userIdx,
            "cluster must come before user in context data");
    }

    [Fact]
    public void FieldOrdering_PreservedAfterUpsert()
    {
        var entry = new ClustralKubeconfigEntry(
            "clustral-dev", "https://dev.example.com", "tok", AnyExpiry,
            InsecureSkipTlsVerify: true);
        _sut.WriteClusterEntry(entry);
        var yaml1 = File.ReadAllText(_kubeconfigPath);

        // Upsert with new token.
        var entry2 = new ClustralKubeconfigEntry(
            "clustral-dev", "https://dev.example.com", "new-tok", AnyExpiry,
            InsecureSkipTlsVerify: true);
        _sut.WriteClusterEntry(entry2);
        var yaml2 = File.ReadAllText(_kubeconfigPath);

        // server should still be before insecure-skip-tls-verify.
        var serverIdx = yaml2.IndexOf("server:", StringComparison.Ordinal);
        var insecureIdx = yaml2.IndexOf("insecure-skip-tls-verify:", StringComparison.Ordinal);
        Assert.True(serverIdx < insecureIdx);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClustralKubeconfigEntry MakeEntry(
        string name = "clustral-test",
        string server = "https://cp.example.com/proxy/test",
        string token = "test-jwt") =>
        new(name, server, token, AnyExpiry);
}
