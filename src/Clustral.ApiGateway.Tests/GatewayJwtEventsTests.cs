using Clustral.ApiGateway.Api;
using Clustral.Sdk.Results;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

namespace Clustral.ApiGateway.Tests;

/// <summary>
/// <see cref="GatewayJwtEvents"/> is a pure classifier — it maps a JWT
/// validation exception to the right <see cref="ResultErrors"/> factory.
/// All user-facing text lives in the SDK catalog, so these tests focus on
/// dispatch correctness, not message content (which is covered in
/// <c>Clustral.Sdk.Tests</c>).
/// </summary>
public sealed class GatewayJwtEventsTests(ITestOutputHelper output)
{
    public static readonly TheoryData<Exception?, string> ExceptionToCode = new()
    {
        { null,                                           "AUTHENTICATION_REQUIRED" },
        { new SecurityTokenExpiredException(),            "INVALID_TOKEN"           },
        { new SecurityTokenInvalidSignatureException(),   "INVALID_TOKEN"           },
        { new SecurityTokenInvalidIssuerException(),      "INVALID_TOKEN"           },
        { new SecurityTokenInvalidAudienceException(),    "INVALID_TOKEN"           },
        { new SecurityTokenNotYetValidException(),        "INVALID_TOKEN"           },
        { new SecurityTokenNoExpirationException(),       "INVALID_TOKEN"           },
        { new SecurityTokenException("custom"),           "INVALID_TOKEN"           },
        { new InvalidOperationException("unknown cause"), "AUTHENTICATION_REQUIRED" },
    };

    [Theory]
    [MemberData(nameof(ExceptionToCode))]
    public void Classify_MapsExceptionToExpectedCode(Exception? ex, string expectedCode)
    {
        var result = GatewayJwtEvents.ClassifyAuthFailure(ex, ResultErrors.LoginHint.Kubectl);

        output.WriteLine($"{ex?.GetType().Name ?? "null"} → {result.Code}: {result.Message}");
        result.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(ResultErrors.LoginHint.Kubectl, "clustral kube login")]
    [InlineData(ResultErrors.LoginHint.Api,     "clustral login")]
    public void Classify_HintControlsRemediationMessage(ResultErrors.LoginHint hint, string expectedPhrase)
    {
        // Every classification must end with the hint appropriate for the
        // request surface (proxy vs REST). This pins that contract across
        // all exception types.
        var scenarios = new Exception?[]
        {
            null,
            new SecurityTokenExpiredException(),
            new SecurityTokenInvalidSignatureException(),
            new SecurityTokenInvalidIssuerException(),
            new SecurityTokenInvalidAudienceException(),
            new SecurityTokenNotYetValidException(),
            new SecurityTokenNoExpirationException(),
            new SecurityTokenException("generic"),
        };

        foreach (var ex in scenarios)
        {
            var result = GatewayJwtEvents.ClassifyAuthFailure(ex, hint);
            result.Message.Should().Contain(expectedPhrase,
                $"classification of {ex?.GetType().Name ?? "null"} with hint {hint} " +
                $"must include the '{expectedPhrase}' remediation");
        }
    }

    [Fact]
    public void Classify_GenericTokenException_IncludesOriginalMessage()
    {
        // The generic SecurityTokenException fallback should carry through
        // the exception message so operators can diagnose unusual failures.
        var result = GatewayJwtEvents.ClassifyAuthFailure(
            new SecurityTokenException("something specific"),
            ResultErrors.LoginHint.Kubectl);

        result.Message.Should().Contain("something specific");
        result.Metadata.Should().NotBeNull();
        result.Metadata!["detail"].Should().Be("something specific");
    }
}
