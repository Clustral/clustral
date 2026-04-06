using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public class AccessTokenTests(ITestOutputHelper output)
{
    [Fact]
    public void IsValid_True_WhenNotExpiredAndNotRevoked()
    {
        var token = new AccessToken
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        };

        output.WriteLine($"ExpiresAt:  +8h");
        output.WriteLine($"RevokedAt:  null");
        output.WriteLine($"IsExpired:  {token.IsExpired}");
        output.WriteLine($"IsRevoked:  {token.IsRevoked}");
        output.WriteLine($"IsValid:    {token.IsValid}");

        Assert.True(token.IsValid);
        Assert.False(token.IsExpired);
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public void IsExpired_True_WhenPastExpiresAt()
    {
        var token = new AccessToken
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };

        output.WriteLine($"ExpiresAt: -1s => IsExpired: {token.IsExpired}");

        Assert.True(token.IsExpired);
        Assert.False(token.IsValid);
    }

    [Fact]
    public void IsRevoked_True_WhenRevokedAtSet()
    {
        var token = new AccessToken
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            RevokedReason = "logout",
        };

        output.WriteLine($"RevokedAt:     -5m");
        output.WriteLine($"RevokedReason: {token.RevokedReason}");
        output.WriteLine($"IsRevoked:     {token.IsRevoked}");
        output.WriteLine($"IsValid:       {token.IsValid}");

        Assert.True(token.IsRevoked);
        Assert.False(token.IsValid);
    }

    [Fact]
    public void IsValid_False_WhenBothExpiredAndRevoked()
    {
        var token = new AccessToken
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
        };

        output.WriteLine("Expired AND revoked => IsValid: false");

        Assert.False(token.IsValid);
    }

    [Fact]
    public void CredentialKind_HasExpectedValues()
    {
        output.WriteLine($"UserKubeconfig: {(int)CredentialKind.UserKubeconfig}");
        output.WriteLine($"Agent:          {(int)CredentialKind.Agent}");

        Assert.Equal(0, (int)CredentialKind.UserKubeconfig);
        Assert.Equal(1, (int)CredentialKind.Agent);
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var token = new AccessToken();

        output.WriteLine($"Kind:          {token.Kind}");
        output.WriteLine($"TokenHash:     \"{token.TokenHash}\"");
        output.WriteLine($"UserId:        {token.UserId?.ToString() ?? "null"}");
        output.WriteLine($"RevokedAt:     {token.RevokedAt?.ToString() ?? "null"}");
        output.WriteLine($"RevokedReason: {token.RevokedReason ?? "null"}");

        Assert.Equal(CredentialKind.UserKubeconfig, token.Kind);
        Assert.Equal(string.Empty, token.TokenHash);
        Assert.Null(token.UserId);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.RevokedReason);
    }

    [Fact]
    public void UserKubeconfig_HasUserId()
    {
        var userId = Guid.NewGuid();
        var token = new AccessToken
        {
            Kind = CredentialKind.UserKubeconfig,
            UserId = userId,
        };

        output.WriteLine($"Kind: UserKubeconfig, UserId: {userId}");

        Assert.Equal(userId, token.UserId);
    }

    [Fact]
    public void Agent_HasNoUserId()
    {
        var token = new AccessToken
        {
            Kind = CredentialKind.Agent,
            UserId = null,
        };

        output.WriteLine($"Kind: Agent, UserId: null");

        Assert.Null(token.UserId);
    }
}
