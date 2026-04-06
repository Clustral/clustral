using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 20: error quality — actionable error messages.
/// </summary>
public sealed class KubeconfigErrorTests
{
    [Fact]
    public void ValidationError_IdentifiesSection()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users: []
            contexts:
            - name: broken
              context:
                cluster: no-such-cluster
                user: no-such-user
            current-context: broken
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml);

        Assert.All(result.Errors, err =>
        {
            Assert.False(string.IsNullOrEmpty(err.Section),
                "Error must identify the failing section");
        });
    }

    [Fact]
    public void ValidationError_IdentifiesEntityName()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users: []
            contexts:
            - name: my-context
              context:
                cluster: missing
                user: missing
            current-context: my-context
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml);

        Assert.All(result.Errors.Where(e => e.Section == "contexts"), err =>
        {
            Assert.Equal("my-context", err.EntityName);
        });
    }

    [Fact]
    public void ValidationError_IdentifiesInvalidField()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users:
            - name: prod
              user:
                token: tok
            contexts:
            - name: prod
              context:
                cluster: no-such-cluster
                user: prod
            current-context: prod
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml);

        var err = Assert.Single(result.Errors, e => e.Code == "DANGLING_CONTEXT_CLUSTER");
        Assert.Equal("cluster", err.Field);
    }

    [Fact]
    public void ValidationError_HasMeaningfulCode()
    {
        var yaml = """
            apiVersion: v1
            kind: Config
            preferences: {}
            clusters: []
            users: []
            contexts: []
            current-context: nonexistent
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml);

        Assert.All(result.Errors, err =>
        {
            Assert.False(string.IsNullOrEmpty(err.Code));
            Assert.NotEqual("invalid config", err.Code); // No vague codes
        });
    }

    [Fact]
    public void ValidationError_MessageDoesNotContainSecrets()
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
                token: eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.secret.payload
                password: super-secret-password-123
            contexts:
            - name: prod
              context:
                cluster: prod
                user: prod
            current-context: prod
            """;

        var result = KubeconfigWriter.ValidateYaml(yaml, KubeconfigSecurityPolicy.Strict);

        Assert.All(result.Errors, err =>
        {
            Assert.DoesNotContain("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9", err.Message);
            Assert.DoesNotContain("super-secret-password-123", err.Message);
        });
    }
}
