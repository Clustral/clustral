using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Tests operate on temp files to avoid touching the developer's real
/// ~/.kube/config.
/// </summary>
public sealed class KubeconfigWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;
    private readonly KubeconfigWriter _sut;

    private static readonly DateTimeOffset AnyExpiry =
        new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public KubeconfigWriterTests()
    {
        _tempDir        = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _kubeconfigPath = Path.Combine(_tempDir, ".kube", "config");
        _sut            = new KubeconfigWriter(_kubeconfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ClustralKubeconfigEntry MakeEntry(
        string contextName = "clustral-test",
        string serverUrl   = "https://cp.example.com/proxy/test",
        string token       = "test-jwt") =>
        new(contextName, serverUrl, token, AnyExpiry);

    // -------------------------------------------------------------------------
    // Fresh kubeconfig
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteClusterEntry_CreatesKubeconfigWhenFileDoesNotExist()
    {
        _sut.WriteClusterEntry(MakeEntry());

        Assert.True(File.Exists(_kubeconfigPath));
    }

    [Fact]
    public void WriteClusterEntry_WritesClusterEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", "https://cp.example.com/proxy/prod", "jwt-abc"));

        var doc = ReadBack();
        Assert.Single(doc.Clusters, c => c.Name == "clustral-prod");
        Assert.Equal("https://cp.example.com/proxy/prod", doc.Clusters[0].Cluster.Server);
    }

    [Fact]
    public void WriteClusterEntry_WritesUserEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "my-token"));

        var doc = ReadBack();
        Assert.Single(doc.Users, u => u.Name == "clustral-prod");
        Assert.Equal("my-token", doc.Users[0].User.Token);
    }

    [Fact]
    public void WriteClusterEntry_WritesContextEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        var doc = ReadBack();
        Assert.Single(doc.Contexts, c => c.Name == "clustral-prod");
        Assert.Equal("clustral-prod", doc.Contexts[0].Context.Cluster);
        Assert.Equal("clustral-prod", doc.Contexts[0].Context.User);
    }

    [Fact]
    public void WriteClusterEntry_SetsCurrentContextByDefault()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        var doc = ReadBack();
        Assert.Equal("clustral-prod", doc.CurrentContext);
    }

    [Fact]
    public void WriteClusterEntry_DoesNotSetCurrentContextWhenFlagIsFalse()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"), setCurrentContext: false);

        var doc = ReadBack();
        // The file was created from scratch so current-context is the default empty string.
        Assert.Equal(string.Empty, doc.CurrentContext);
    }

    // -------------------------------------------------------------------------
    // Upsert behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteClusterEntry_UpdatesExistingCluster()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", serverUrl: "https://old.example.com"));
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", serverUrl: "https://new.example.com"));

        var doc = ReadBack();
        Assert.Single(doc.Clusters);
        Assert.Equal("https://new.example.com", doc.Clusters[0].Cluster.Server);
    }

    [Fact]
    public void WriteClusterEntry_UpdatesExistingToken()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "old-token"));
        _sut.WriteClusterEntry(MakeEntry("clustral-prod", token: "new-token"));

        var doc = ReadBack();
        Assert.Single(doc.Users);
        Assert.Equal("new-token", doc.Users[0].User.Token);
    }

    [Fact]
    public void WriteClusterEntry_PreservesOtherClusters()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-dev"));
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        var doc = ReadBack();
        Assert.Equal(2, doc.Clusters.Count);
        Assert.Contains(doc.Clusters, c => c.Name == "clustral-dev");
        Assert.Contains(doc.Clusters, c => c.Name == "clustral-prod");
    }

    [Fact]
    public void WriteClusterEntry_PreservesExistingCurrentContextWhenFlagIsFalse()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-dev"), setCurrentContext: true);
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"), setCurrentContext: false);

        var doc = ReadBack();
        Assert.Equal("clustral-dev", doc.CurrentContext);
    }

    // -------------------------------------------------------------------------
    // Pre-existing kubeconfig (simulates a file already used by other tools)
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteClusterEntry_PreservesUnrelatedYamlContent()
    {
        var preExisting = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: minikube
              cluster:
                server: https://192.168.64.2:8443
            users:
            - name: minikube
              user:
                token: old-minikube-token
            contexts:
            - name: minikube
              context:
                cluster: minikube
                user: minikube
            current-context: minikube
            """;

        Directory.CreateDirectory(Path.GetDirectoryName(_kubeconfigPath)!);
        File.WriteAllText(_kubeconfigPath, preExisting);

        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        var doc = ReadBack();
        Assert.Equal(2, doc.Clusters.Count);
        Assert.Contains(doc.Clusters, c => c.Name == "minikube");
        Assert.Contains(doc.Clusters, c => c.Name == "clustral-prod");
        // Adding a new cluster with setCurrentContext=true changes current-context.
        Assert.Equal("clustral-prod", doc.CurrentContext);
    }

    // -------------------------------------------------------------------------
    // RemoveClusterEntry
    // -------------------------------------------------------------------------

    [Fact]
    public void RemoveClusterEntry_RemovesAllThreeEntries()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        _sut.RemoveClusterEntry("clustral-prod");

        var doc = ReadBack();
        Assert.Empty(doc.Clusters);
        Assert.Empty(doc.Users);
        Assert.Empty(doc.Contexts);
    }

    [Fact]
    public void RemoveClusterEntry_ClearsCurrentContextWhenItWasRemoved()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        _sut.RemoveClusterEntry("clustral-prod");

        var doc = ReadBack();
        Assert.Equal(string.Empty, doc.CurrentContext);
    }

    [Fact]
    public void RemoveClusterEntry_FallsBackToFirstRemainingContext()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-dev"));
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"), setCurrentContext: true);

        _sut.RemoveClusterEntry("clustral-prod");

        var doc = ReadBack();
        Assert.Equal("clustral-dev", doc.CurrentContext);
    }

    [Fact]
    public void RemoveClusterEntry_DoesNotTouchOtherEntries()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-dev"));
        _sut.WriteClusterEntry(MakeEntry("clustral-prod"));

        _sut.RemoveClusterEntry("clustral-prod");

        var doc = ReadBack();
        Assert.Single(doc.Clusters, c => c.Name == "clustral-dev");
        Assert.Single(doc.Users,    u => u.Name == "clustral-dev");
        Assert.Single(doc.Contexts, c => c.Name == "clustral-dev");
    }

    [Fact]
    public void RemoveClusterEntry_IsIdempotentForNonExistentEntry()
    {
        _sut.WriteClusterEntry(MakeEntry("clustral-dev"));

        // Should not throw.
        _sut.RemoveClusterEntry("does-not-exist");

        var doc = ReadBack();
        Assert.Single(doc.Clusters);
    }

    // -------------------------------------------------------------------------
    // InsecureSkipTlsVerify
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteClusterEntry_SetsInsecureSkipTlsVerifyWhenRequested()
    {
        var entry = new ClustralKubeconfigEntry(
            "clustral-kind",
            "http://localhost:8080",
            "tok",
            AnyExpiry,
            InsecureSkipTlsVerify: true);

        _sut.WriteClusterEntry(entry);

        var doc = ReadBack();
        Assert.True(doc.Clusters[0].Cluster.InsecureSkipTlsVerify);
    }

    // -------------------------------------------------------------------------
    // Round-trip integrity
    // -------------------------------------------------------------------------

    [Fact]
    public void WriteClusterEntry_ProducesValidYaml()
    {
        _sut.WriteClusterEntry(MakeEntry());

        var yaml = File.ReadAllText(_kubeconfigPath);

        // Minimum expected keys in the output.
        Assert.Contains("apiVersion: v1", yaml);
        Assert.Contains("kind: Config", yaml);
        Assert.Contains("clusters:", yaml);
        Assert.Contains("users:", yaml);
        Assert.Contains("contexts:", yaml);
    }

    // -------------------------------------------------------------------------
    // Private helper
    // -------------------------------------------------------------------------

    private KubeconfigDocument ReadBack()
    {
        var yaml = File.ReadAllText(_kubeconfigPath);
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<KubeconfigDocument>(yaml);
    }
}
