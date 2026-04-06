using Clustral.Sdk.Kubeconfig;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Categories 1, 2, 7: serialization correctness, deterministic output, minimal config.
/// </summary>
public sealed class KubeconfigSerializationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly KubeconfigWriter _sut;

    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public KubeconfigSerializationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-serial-{Guid.NewGuid():N}");
        _kubeconfigPath = Path.Combine(_tempDir, "config");
        _sut = new KubeconfigWriter(_kubeconfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Category 1: Serialization correctness ───────────────────────────────

    [Fact]
    public void Serialize_MinimalValidConfig_ContainsApiVersionV1()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("apiVersion: v1", yaml);
    }

    [Fact]
    public void Serialize_MinimalValidConfig_ContainsKindConfig()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("kind: Config", yaml);
    }

    [Fact]
    public void Serialize_MinimalValidConfig_ContainsPreferences()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("preferences:", yaml);
    }

    [Fact]
    public void Serialize_MinimalValidConfig_EmitsClustersUsersContextsAsLists()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);

        Assert.Contains("clusters:", yaml);
        Assert.Contains("users:", yaml);
        Assert.Contains("contexts:", yaml);

        // Each list should contain at least one entry with "- name:"
        Assert.Contains("- name:", yaml);
    }

    [Fact]
    public void Serialize_MinimalValidConfig_EmitsCurrentContext()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-test"));
        var yaml = File.ReadAllText(_kubeconfigPath);
        Assert.Contains("current-context: clustral-test", yaml);
    }

    [Fact]
    public void Serialize_Output_IsParseableYaml()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", "https://cp.example.com/proxy/prod", "jwt-xyz"));

        var yaml = File.ReadAllText(_kubeconfigPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var doc = deserializer.Deserialize<KubeconfigDocument>(yaml);

        Assert.NotNull(doc);
        Assert.Equal("v1", doc.ApiVersion);
        Assert.Equal("Config", doc.Kind);
        Assert.Single(doc.Clusters);
        Assert.Single(doc.Users);
        Assert.Single(doc.Contexts);
    }

    [Fact]
    public void Serialize_Output_PreservesReferencesAfterRoundTrip()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        var yaml = File.ReadAllText(_kubeconfigPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var doc = deserializer.Deserialize<KubeconfigDocument>(yaml);

        // Context references must match cluster and user names.
        Assert.Equal("clustral-prod", doc.Contexts[0].Context.Cluster);
        Assert.Equal("clustral-prod", doc.Contexts[0].Context.User);
        Assert.Equal("clustral-prod", doc.CurrentContext);
    }

    // ── Category 2: Deterministic output ────────────────────────────────────

    [Fact]
    public void Serialize_SameSemanticInputTwice_ProducesByteIdenticalOutput()
    {
        // Write first config.
        _sut.WriteClusterEntry(MakeEntry("clustral-alpha", "https://a.example.com", "token-a"));
        _sut.WriteClusterEntry(MakeEntry("clustral-beta", "https://b.example.com", "token-b"));
        var output1 = File.ReadAllText(_kubeconfigPath);

        // Delete and recreate the same config from scratch.
        File.Delete(_kubeconfigPath);
        var sut2 = new KubeconfigWriter(_kubeconfigPath);
        sut2.WriteClusterEntry(MakeEntry("clustral-alpha", "https://a.example.com", "token-a"));
        sut2.WriteClusterEntry(MakeEntry("clustral-beta", "https://b.example.com", "token-b"));
        var output2 = File.ReadAllText(_kubeconfigPath);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public void Serialize_DifferentInsertionOrder_ProducesIdenticalOutput()
    {
        // Add in order alpha, beta (don't set current-context to avoid order dependency).
        _sut.WriteClusterEntry(MakeEntry("clustral-alpha", "https://a.example.com", "token-a"), setCurrentContext: false);
        _sut.WriteClusterEntry(MakeEntry("clustral-beta", "https://b.example.com", "token-b"), setCurrentContext: false);
        var output1 = File.ReadAllText(_kubeconfigPath);

        // Delete and add in order beta, alpha.
        File.Delete(_kubeconfigPath);
        var sut2 = new KubeconfigWriter(_kubeconfigPath);
        sut2.WriteClusterEntry(MakeEntry("clustral-beta", "https://b.example.com", "token-b"), setCurrentContext: false);
        sut2.WriteClusterEntry(MakeEntry("clustral-alpha", "https://a.example.com", "token-a"), setCurrentContext: false);
        var output2 = File.ReadAllText(_kubeconfigPath);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public void Serialize_OutputContainsNoTimestampsOrRandomIds()
    {
        _sut.WriteClusterEntry(MakeEntry());
        var yaml = File.ReadAllText(_kubeconfigPath);

        // Should not contain ISO date patterns or UUID-like strings.
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", yaml);
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", yaml);
    }

    // ── Category 7: Minimal valid kubeconfig ────────────────────────────────

    [Fact]
    public void Serialize_MinimalConfig_SingleClusterUserContext()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-minimal", "https://min.example.com", "min-token"));

        var yaml = File.ReadAllText(_kubeconfigPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var doc = deserializer.Deserialize<KubeconfigDocument>(yaml);

        Assert.Single(doc.Clusters);
        Assert.Single(doc.Users);
        Assert.Single(doc.Contexts);
        Assert.Equal("clustral-minimal", doc.CurrentContext);
        Assert.Equal("https://min.example.com", doc.Clusters[0].Cluster.Server);
        Assert.Equal("min-token", doc.Users[0].User.Token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClustralKubeconfigEntry MakeEntry(
        string name = "clustral-test",
        string server = "https://cp.example.com/proxy/test",
        string token = "test-jwt") =>
        new(name, server, token, AnyExpiry);
}
