using Clustral.Sdk.Http;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Http;

public class ErrorDocumentationTests(ITestOutputHelper output) : IDisposable
{
    public void Dispose() => ErrorDocumentation.SetBaseUrl(ErrorDocumentation.DefaultBaseUrl);

    [Fact]
    public void UrlFor_DefaultBase_KebabCasesScreamingSnakeCode()
    {
        var url = ErrorDocumentation.UrlFor("NO_ROLE_ASSIGNMENT");

        output.WriteLine(url);
        url.Should().Be("https://docs.clustral.kube.it.com/errors/no-role-assignment");
    }

    [Fact]
    public void UrlFor_MixedCaseCode_LowerCasedAndHyphenated()
    {
        ErrorDocumentation.UrlFor("Cluster_Mismatch")
            .Should().Be("https://docs.clustral.kube.it.com/errors/cluster-mismatch");
    }

    [Fact]
    public void UrlFor_EmptyCode_ReturnsBaseUrl()
    {
        ErrorDocumentation.UrlFor("").Should().Be(ErrorDocumentation.DefaultBaseUrl);
    }

    [Fact]
    public void SetBaseUrl_OverridesForAirGappedDeployments()
    {
        ErrorDocumentation.SetBaseUrl("https://internal.corp/clustral-errors/");

        ErrorDocumentation.UrlFor("AGENT_NOT_CONNECTED")
            .Should().Be("https://internal.corp/clustral-errors/agent-not-connected");
    }

    [Fact]
    public void SetBaseUrl_TrailingSlashAdded_WhenOmitted()
    {
        ErrorDocumentation.SetBaseUrl("https://internal.corp/clustral-errors");

        ErrorDocumentation.UrlFor("X").Should().Be("https://internal.corp/clustral-errors/x");
    }

    [Fact]
    public void SetBaseUrl_NullOrEmpty_KeepsCurrentValue()
    {
        var before = ErrorDocumentation.BaseUrl;
        ErrorDocumentation.SetBaseUrl(null);
        ErrorDocumentation.SetBaseUrl("");
        ErrorDocumentation.BaseUrl.Should().Be(before);
    }
}
