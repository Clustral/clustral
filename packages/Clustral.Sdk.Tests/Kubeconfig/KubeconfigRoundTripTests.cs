using Clustral.Sdk.Kubeconfig;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 18: round-trip semantic validity.
/// </summary>
public sealed class KubeconfigRoundTripTests : IDisposable
{
    private readonly string _tempDir;
    private static readonly DateTimeOffset AnyExpiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public KubeconfigRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void RoundTrip_TokenAuth_PreservesSemantics()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com/proxy/prod", "jwt-token", AnyExpiry));

        var yaml = File.ReadAllText(path);
        var doc = Deserializer.Deserialize<KubeconfigDocument>(yaml);

        Assert.Equal("v1", doc.ApiVersion);
        Assert.Equal("Config", doc.Kind);
        Assert.Equal("clustral-prod", doc.CurrentContext);
        Assert.Single(doc.Clusters);
        Assert.Equal("https://cp.example.com/proxy/prod", doc.Clusters[0].Cluster.Server);
        Assert.Equal("jwt-token", doc.Users[0].User.Token);
        Assert.Equal("clustral-prod", doc.Contexts[0].Context.Cluster);
        Assert.Equal("clustral-prod", doc.Contexts[0].Context.User);
    }

    [Fact]
    public void RoundTrip_MultipleEntries_ReferencesStillMatch()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry("clustral-dev", "https://dev.example.com", "dev-tok", AnyExpiry), setCurrentContext: false);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry("clustral-prod", "https://prod.example.com", "prod-tok", AnyExpiry), setCurrentContext: true);

        var yaml = File.ReadAllText(path);
        var doc = Deserializer.Deserialize<KubeconfigDocument>(yaml);

        // Each context should reference its own cluster and user.
        foreach (var ctx in doc.Contexts)
        {
            Assert.Contains(doc.Clusters, c => c.Name == ctx.Context.Cluster);
            Assert.Contains(doc.Users, u => u.Name == ctx.Context.User);
        }

        // current-context should exist in contexts.
        Assert.Contains(doc.Contexts, c => c.Name == doc.CurrentContext);
    }

    [Fact]
    public void RoundTrip_WriteReadWrite_ProducesIdenticalOutput()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry("clustral-test", "https://test.example.com", "tok", AnyExpiry));
        var output1 = File.ReadAllText(path);

        // Read and write again (upsert with same data).
        sut.WriteClusterEntry(new ClustralKubeconfigEntry("clustral-test", "https://test.example.com", "tok", AnyExpiry));
        var output2 = File.ReadAllText(path);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public void RoundTrip_PreservesUnknownFields()
    {
        var path = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            apiVersion: v1
            kind: Config
            preferences:
              colors: true
            clusters:
            - name: minikube
              cluster:
                server: https://192.168.49.2:8443
                certificate-authority: /home/user/.minikube/ca.crt
            users:
            - name: minikube
              user:
                client-certificate: /path/cert.pem
                client-key: /path/key.pem
            contexts:
            - name: minikube
              context:
                cluster: minikube
                user: minikube
                namespace: kube-system
            current-context: minikube
            """);

        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry("clustral-prod", "https://prod.example.com", "tok", AnyExpiry));

        var yaml = File.ReadAllText(path);

        // All minikube fields should survive.
        Assert.Contains("certificate-authority:", yaml);
        Assert.Contains("client-certificate:", yaml);
        Assert.Contains("client-key:", yaml);
        Assert.Contains("namespace: kube-system", yaml);
        Assert.Contains("minikube", yaml);
    }

    [Fact]
    public void RoundTrip_ValidationPassesAfterWrite()
    {
        var path = Path.Combine(_tempDir, "config");
        var sut = new KubeconfigWriter(path);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry("clustral-a", "https://a.example.com", "tok-a", AnyExpiry));
        sut.WriteClusterEntry(new ClustralKubeconfigEntry("clustral-b", "https://b.example.com", "tok-b", AnyExpiry));

        var result = sut.Validate();
        Assert.True(result.IsValid);
    }
}
