using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 13: security policy enforcement.
/// </summary>
public sealed class KubeconfigSecurityPolicyTests
{
    [Fact]
    public void Policy_ForbidInsecureSkipTls_RejectsInsecureCluster()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: dev
              cluster:
                server: https://dev.example.com
                insecure-skip-tls-verify: true
            users:
            - name: dev
              user:
                token: tok
            contexts:
            - name: dev
              context:
                cluster: dev
                user: dev
            current-context: dev
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, new KubeconfigSecurityPolicy
        {
            ForbidInsecureSkipTls = true,
        });

        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors, e => e.Code == "INSECURE_TLS_FORBIDDEN");
        Assert.Equal("clusters", err.Section);
        Assert.Equal("dev", err.EntityName);
    }

    [Fact]
    public void Policy_ForbidPlaintextServer_RejectsHttpServer()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: local
              cluster:
                server: http://localhost:8080
            users:
            - name: local
              user:
                token: tok
            contexts:
            - name: local
              context:
                cluster: local
                user: local
            current-context: local
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, new KubeconfigSecurityPolicy
        {
            ForbidPlaintextServer = true,
        });

        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors, e => e.Code == "PLAINTEXT_SERVER_FORBIDDEN");
        Assert.Contains("plaintext", err.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_ForbidStaticPasswords_RejectsPasswordAuth()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: legacy
              cluster:
                server: https://legacy.example.com
            users:
            - name: legacy
              user:
                username: admin
                password: secret123
            contexts:
            - name: legacy
              context:
                cluster: legacy
                user: legacy
            current-context: legacy
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, new KubeconfigSecurityPolicy
        {
            ForbidStaticPasswords = true,
        });

        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors, e => e.Code == "STATIC_PASSWORD_FORBIDDEN");
        Assert.Equal("users", err.Section);
        Assert.Equal("legacy", err.EntityName);
    }

    [Fact]
    public void Policy_ForbidStaticTokens_RejectsTokenAuth()
    {
        var yaml = """
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
                token: my-static-token
            contexts:
            - name: prod
              context:
                cluster: prod
                user: prod
            current-context: prod
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, new KubeconfigSecurityPolicy
        {
            ForbidStaticTokens = true,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "STATIC_TOKEN_FORBIDDEN");
    }

    [Fact]
    public void Policy_RequireNamespaceOnContexts_RejectsMissingNamespace()
    {
        var yaml = """
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
            current-context: prod
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, new KubeconfigSecurityPolicy
        {
            RequireNamespaceOnContexts = true,
        });

        Assert.False(result.IsValid);
        var err = Assert.Single(result.Errors, e => e.Code == "NAMESPACE_REQUIRED");
        Assert.Equal("prod", err.EntityName);
    }

    [Fact]
    public void Policy_Permissive_AllowsEverything()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: dev
              cluster:
                server: http://localhost:8080
                insecure-skip-tls-verify: true
            users:
            - name: dev
              user:
                token: tok
                password: pw
            contexts:
            - name: dev
              context:
                cluster: dev
                user: dev
            current-context: dev
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, KubeconfigSecurityPolicy.Permissive);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Policy_Strict_RejectsMultipleViolations()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters:
            - name: bad
              cluster:
                server: http://bad.example.com
                insecure-skip-tls-verify: true
            users:
            - name: bad
              user:
                token: tok
                password: pw
            contexts:
            - name: bad
              context:
                cluster: bad
                user: bad
            current-context: bad
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, KubeconfigSecurityPolicy.Strict);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3, $"Expected at least 3 errors, got {result.Errors.Count}");
        Assert.Contains(result.Errors, e => e.Code == "INSECURE_TLS_FORBIDDEN");
        Assert.Contains(result.Errors, e => e.Code == "PLAINTEXT_SERVER_FORBIDDEN");
        Assert.Contains(result.Errors, e => e.Code == "STATIC_PASSWORD_FORBIDDEN");
    }
}
