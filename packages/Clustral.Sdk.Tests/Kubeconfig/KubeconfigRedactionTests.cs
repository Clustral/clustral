using Clustral.Sdk.Kubeconfig;

namespace Clustral.Sdk.Tests.Kubeconfig;

/// <summary>
/// Category 14: sensitive data redaction.
/// </summary>
public sealed class KubeconfigRedactionTests
{
    [Fact]
    public void RedactYaml_RedactsTokenValue()
    {
        var yaml = """
            users:
            - name: prod
              user:
                token: eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.secret
            """;

        var redacted = KubeconfigRedactor.RedactYaml(yaml);

        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9", redacted);
        Assert.Contains($"token: {KubeconfigRedactor.RedactedPlaceholder}", redacted);
    }

    [Fact]
    public void RedactYaml_RedactsPasswordValue()
    {
        var yaml = """
            users:
            - name: legacy
              user:
                username: admin
                password: super-secret-123
            """;

        var redacted = KubeconfigRedactor.RedactYaml(yaml);

        Assert.DoesNotContain("super-secret-123", redacted);
        Assert.Contains($"password: {KubeconfigRedactor.RedactedPlaceholder}", redacted);
    }

    [Fact]
    public void RedactYaml_RedactsClientKeyData()
    {
        var yaml = """
            users:
            - name: cert-user
              user:
                client-key-data: LS0tLS1CRUdJTiBSU0EgUFJJVkFURSBLRVktLS0tLQ==
                client-certificate-data: LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0t
            """;

        var redacted = KubeconfigRedactor.RedactYaml(yaml);

        Assert.DoesNotContain("LS0tLS1CRUdJTiBSU0E", redacted);
        Assert.Contains($"client-key-data: {KubeconfigRedactor.RedactedPlaceholder}", redacted);
        Assert.Contains($"client-certificate-data: {KubeconfigRedactor.RedactedPlaceholder}", redacted);
    }

    [Fact]
    public void RedactDocument_RedactsTokenInUserData()
    {
        var doc = new Dictionary<object, object>
        {
            ["users"] = new List<object>
            {
                new Dictionary<object, object>
                {
                    ["name"] = "prod",
                    ["user"] = new Dictionary<object, object>
                    {
                        ["token"] = "secret-jwt-token",
                    },
                },
            },
        };

        var redacted = KubeconfigRedactor.RedactDocument(doc);

        // Original is untouched.
        var originalUser = ((List<object>)doc["users"])[0] as Dictionary<object, object>;
        Assert.Equal("secret-jwt-token", ((Dictionary<object, object>)originalUser!["user"])["token"]);

        // Redacted copy has placeholder.
        var redactedUser = ((List<object>)redacted["users"])[0] as Dictionary<object, object>;
        Assert.Equal(KubeconfigRedactor.RedactedPlaceholder, ((Dictionary<object, object>)redactedUser!["user"])["token"]);
    }

    [Fact]
    public void RedactDocument_RedactsExecEnvValues()
    {
        var doc = new Dictionary<object, object>
        {
            ["users"] = new List<object>
            {
                new Dictionary<object, object>
                {
                    ["name"] = "exec-user",
                    ["user"] = new Dictionary<object, object>
                    {
                        ["exec"] = new Dictionary<object, object>
                        {
                            ["command"] = "aws-iam-authenticator",
                            ["env"] = new List<object>
                            {
                                new Dictionary<object, object>
                                {
                                    ["name"] = "AWS_PROFILE",
                                    ["value"] = "sensitive-profile-name",
                                },
                            },
                        },
                    },
                },
            },
        };

        var redacted = KubeconfigRedactor.RedactDocument(doc);

        var redactedUser = ((List<object>)redacted["users"])[0] as Dictionary<object, object>;
        var exec = ((Dictionary<object, object>)redactedUser!["user"])["exec"] as Dictionary<object, object>;
        var env = (exec!["env"] as List<object>)![0] as Dictionary<object, object>;
        Assert.Equal(KubeconfigRedactor.RedactedPlaceholder, env!["value"]);
    }
}
