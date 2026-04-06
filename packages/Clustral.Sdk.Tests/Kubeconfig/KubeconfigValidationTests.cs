using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Categories 9, 10: reference integrity validation and duplicate name handling.
/// </summary>
public sealed class KubeconfigValidationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kubeconfigPath;

    public KubeconfigValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kubeconfig-val-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _kubeconfigPath = Path.Combine(_tempDir, "config");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Category 9: Reference integrity ─────────────────────────────────────

    [Fact]
    public void Validate_ValidConfig_ReturnsSuccess()
    {
        var sut = new KubeconfigWriter(_kubeconfigPath);
        sut.WriteClusterEntry(new ClustralKubeconfigEntry(
            "clustral-prod", "https://cp.example.com", "tok", DateTimeOffset.UtcNow.AddHours(8)));

        var result = sut.Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ContextReferencesMissingCluster_FailsWithActionableError()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users:
            - name: prod-user
              user:
                token: tok
            contexts:
            - name: prod-ctx
              context:
                cluster: missing-cluster
                user: prod-user
            current-context: prod-ctx
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();

        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors, e => e.Code == "DANGLING_CONTEXT_CLUSTER");
        Assert.Equal("contexts", err.Section);
        Assert.Equal("prod-ctx", err.EntityName);
        Assert.Equal("cluster", err.Field);
        Assert.Contains("missing-cluster", err.Message);
    }

    [Fact]
    public void Validate_ContextReferencesMissingUser_FailsWithActionableError()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: prod-cluster
              cluster:
                server: https://prod.example.com
            users: []
            contexts:
            - name: prod-ctx
              context:
                cluster: prod-cluster
                user: missing-user
            current-context: prod-ctx
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();

        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors, e => e.Code == "DANGLING_CONTEXT_USER");
        Assert.Equal("missing-user", err.Message.Split('\'')[3]);
    }

    [Fact]
    public void Validate_CurrentContextReferencesMissingContext_Fails()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users: []
            contexts: []
            current-context: nonexistent
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();

        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors, e => e.Code == "DANGLING_CURRENT_CONTEXT");
        Assert.Contains("nonexistent", err.Message);
    }

    [Fact]
    public void Validate_EmptyCurrentContext_IsValid()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users: []
            contexts: []
            current-context: ""
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MultipleErrors_ReportsAll()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users: []
            contexts:
            - name: ctx1
              context:
                cluster: missing1
                user: missing2
            - name: ctx2
              context:
                cluster: missing3
                user: missing4
            current-context: nonexistent
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();

        Assert.False(result.IsValid);
        // 4 dangling references + 1 dangling current-context = 5 errors
        Assert.Equal(5, result.Errors.Count);
    }

    // ── Category 10: Duplicate name handling ────────────────────────────────

    [Fact]
    public void Validate_DuplicateClusterNames_ReportsError()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: prod
              cluster:
                server: https://a.example.com
            - name: prod
              cluster:
                server: https://b.example.com
            users:
            - name: prod
              user:
                token: tok
            contexts:
            - name: prod
              context:
                cluster: prod
                user: prod
            current-context: prod
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();

        Assert.False(result.IsValid);
        var dup = Assert.Single(result.Errors, e => e.Code == "DUPLICATE_NAME" && e.Section == "clusters");
        Assert.Contains("prod", dup.Message);
    }

    [Fact]
    public void Validate_DuplicateUserNames_ReportsError()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: prod
              cluster:
                server: https://prod.example.com
            users:
            - name: prod
              user:
                token: tok1
            - name: prod
              user:
                token: tok2
            contexts:
            - name: prod
              context:
                cluster: prod
                user: prod
            current-context: prod
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DUPLICATE_NAME" && e.Section == "users");
    }

    [Fact]
    public void Validate_DuplicateContextNames_ReportsError()
    {
        WriteRawYaml("""
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: prod
              cluster:
                server: https://prod.example.com
            users:
            - name: prod
              user:
                token: tok
            contexts:
            - name: prod
              context:
                cluster: prod
                user: prod
            - name: prod
              context:
                cluster: prod
                user: prod
            current-context: prod
            """);

        var result = new KubeconfigWriter(_kubeconfigPath).Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DUPLICATE_NAME" && e.Section == "contexts");
    }

    // ── Static YAML validation ──────────────────────────────────────────────

    [Fact]
    public void ValidateYaml_ValidString_ReturnsSuccess()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: test
              cluster:
                server: https://test.example.com
            users:
            - name: test
              user:
                token: tok
            contexts:
            - name: test
              context:
                cluster: test
                user: test
            current-context: test
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml);
        Assert.True(result.IsValid);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void WriteRawYaml(string yaml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_kubeconfigPath)!);
        File.WriteAllText(_kubeconfigPath, yaml);
    }
}
