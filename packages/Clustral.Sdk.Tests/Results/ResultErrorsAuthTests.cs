using Clustral.Sdk.Results;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Sdk.Tests.Results;

/// <summary>
/// Covers the auth-related <see cref="ResultErrors"/> factories (gateway
/// JWT validation + generic authn/authz). These are the "single source of
/// truth" for user-facing auth error text — asserting them here means the
/// gateway's <c>GatewayJwtEvents</c> never needs to duplicate the strings.
/// </summary>
public class ResultErrorsAuthTests(ITestOutputHelper output)
{
    // Every auth factory has the same structural contract:
    //   - Kind = Unauthorized (or Forbidden for AuthorizationFailed)
    //   - Code in { AUTHENTICATION_REQUIRED, INVALID_TOKEN, FORBIDDEN }
    //   - Message ends with the hint matching the LoginHint parameter
    //   - Kubectl hint ⇒ "clustral kube login"; Api hint ⇒ "clustral login"

    public static readonly TheoryData<Func<ResultErrors.LoginHint, ResultError>, string, string> Factories = new()
    {
        { h => ResultErrors.AuthenticationRequired(h),      "AUTHENTICATION_REQUIRED", "missing" },
        { h => ResultErrors.TokenExpired(h),                "INVALID_TOKEN",           "expired" },
        { h => ResultErrors.TokenInvalidSignature(h),       "INVALID_TOKEN",           "signature" },
        { h => ResultErrors.TokenInvalidIssuer(h),          "INVALID_TOKEN",           "issuer" },
        { h => ResultErrors.TokenInvalidAudience(h),        "INVALID_TOKEN",           "audience" },
        { h => ResultErrors.TokenNotYetValid(h),            "INVALID_TOKEN",           "not yet valid" },
        { h => ResultErrors.TokenMissingExpiration(h),      "INVALID_TOKEN",           "expiration" },
        { h => ResultErrors.TokenValidationFailed("x", h),  "INVALID_TOKEN",           "rejected" },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_ProducesUnauthorizedKindAndExpectedCode(
        Func<ResultErrors.LoginHint, ResultError> factory, string expectedCode, string _)
    {
        var error = factory(ResultErrors.LoginHint.Kubectl);
        error.Kind.Should().Be(ResultErrorKind.Unauthorized);
        error.Code.Should().Be(expectedCode);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_MessageContainsDistinguishingPhrase(
        Func<ResultErrors.LoginHint, ResultError> factory, string _, string expectedPhrase)
    {
        var error = factory(ResultErrors.LoginHint.Kubectl);
        output.WriteLine($"{error.Code}: {error.Message}");
        error.Message.Should().Contain(expectedPhrase,
            "each factory message must name its specific failure mode");
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_KubectlHint_MessageEndsWithKubectlRemediation(
        Func<ResultErrors.LoginHint, ResultError> factory, string _, string __)
    {
        var error = factory(ResultErrors.LoginHint.Kubectl);
        error.Message.Should().Contain("clustral kube login");
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_ApiHint_MessageEndsWithApiRemediation(
        Func<ResultErrors.LoginHint, ResultError> factory, string _, string __)
    {
        var error = factory(ResultErrors.LoginHint.Api);
        // "clustral login" without "kube" — the API-side remediation.
        error.Message.Should().Contain("clustral login");
        error.Message.Should().NotContain("clustral kube login");
    }

    [Fact]
    public void TokenValidationFailed_IncludesDetailInMessageAndMetadata()
    {
        var error = ResultErrors.TokenValidationFailed("unexpected issuer foo", ResultErrors.LoginHint.Kubectl);

        error.Message.Should().Contain("unexpected issuer foo");
        error.Metadata.Should().NotBeNull();
        error.Metadata!["detail"].Should().Be("unexpected issuer foo");
    }

    [Fact]
    public void AuthorizationFailed_Kubectl_MentionsAccessRequest()
    {
        var error = ResultErrors.AuthorizationFailed(ResultErrors.LoginHint.Kubectl);

        output.WriteLine(error.Message);
        error.Kind.Should().Be(ResultErrorKind.Forbidden);
        error.Code.Should().Be("FORBIDDEN");
        error.Message.Should().Contain("clustral access request");
    }

    [Fact]
    public void AuthorizationFailed_Api_DoesNotMentionClustralAccessRequest()
    {
        var error = ResultErrors.AuthorizationFailed(ResultErrors.LoginHint.Api);

        error.Kind.Should().Be(ResultErrorKind.Forbidden);
        error.Code.Should().Be("FORBIDDEN");
        // REST API callers can't do JIT access requests for the resource
        // they're hitting — the message reflects that.
        error.Message.Should().NotContain("clustral access request");
    }

    [Fact]
    public void AuthenticationRequired_DefaultsToKubectlHint()
    {
        // Backward-compat with existing proxy-path callers (ProxyAuthService).
        var error = ResultErrors.AuthenticationRequired();
        error.Message.Should().Contain("clustral kube login");
    }
}
