using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public class AccessRequestTests(ITestOutputHelper output)
{
    // ── IsPendingExpired ─────────────────────────────────────────────────────

    [Fact]
    public void IsPendingExpired_True_WhenPendingAndPastTtl()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Pending,
            RequestExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        output.WriteLine($"Status: {request.Status}, ExpiresAt: {request.RequestExpiresAt}, Now: {DateTimeOffset.UtcNow}");
        output.WriteLine($"IsPendingExpired: {request.IsPendingExpired}");

        Assert.True(request.IsPendingExpired);
    }

    [Fact]
    public void IsPendingExpired_False_WhenPendingAndFutureTtl()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Pending,
            RequestExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };

        output.WriteLine($"IsPendingExpired: {request.IsPendingExpired} (TTL still valid)");

        Assert.False(request.IsPendingExpired);
    }

    [Fact]
    public void IsPendingExpired_False_WhenApprovedEvenIfPastTtl()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            RequestExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30),
        };

        output.WriteLine($"Status: Approved, ExpiresAt in past => IsPendingExpired: {request.IsPendingExpired}");

        Assert.False(request.IsPendingExpired);
    }

    [Fact]
    public void IsPendingExpired_False_WhenDenied()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Denied,
            RequestExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        };

        Assert.False(request.IsPendingExpired);
    }

    // ── IsGrantActive ────────────────────────────────────────────────────────

    [Fact]
    public void IsGrantActive_True_WhenApprovedAndGrantNotExpired()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
        };

        output.WriteLine($"Status: Approved, GrantExpiresAt: +4h => IsGrantActive: {request.IsGrantActive}");

        Assert.True(request.IsGrantActive);
    }

    [Fact]
    public void IsGrantActive_False_WhenApprovedButGrantExpired()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };

        output.WriteLine($"GrantExpiresAt: -1s => IsGrantActive: {request.IsGrantActive}");

        Assert.False(request.IsGrantActive);
    }

    [Fact]
    public void IsGrantActive_False_WhenPending()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Pending,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        };

        Assert.False(request.IsGrantActive);
    }

    [Fact]
    public void IsGrantActive_False_WhenGrantExpiresAtIsNull()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = null,
        };

        Assert.False(request.IsGrantActive);
    }

    // ── Defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreCorrect()
    {
        var request = new AccessRequest();

        output.WriteLine($"Status:            {request.Status}");
        output.WriteLine($"RequestedDuration: {request.RequestedDuration}");
        output.WriteLine($"Reason:            \"{request.Reason}\"");
        output.WriteLine($"SuggestedReviewers: {request.SuggestedReviewers.Count} items");
        output.WriteLine($"ReviewerId:        {request.ReviewerId?.ToString() ?? "null"}");
        output.WriteLine($"DenialReason:      {request.DenialReason ?? "null"}");
        output.WriteLine($"GrantExpiresAt:    {request.GrantExpiresAt?.ToString() ?? "null"}");

        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal(TimeSpan.FromHours(8), request.RequestedDuration);
        Assert.Equal(string.Empty, request.Reason);
        Assert.Empty(request.SuggestedReviewers);
        Assert.Null(request.ReviewerId);
        Assert.Null(request.DenialReason);
        Assert.Null(request.GrantExpiresAt);
    }

    // ── Enum values ──────────────────────────────────────────────────────────

    [Fact]
    public void StatusEnum_HasExpectedValues()
    {
        output.WriteLine($"Pending:  {(int)AccessRequestStatus.Pending}");
        output.WriteLine($"Approved: {(int)AccessRequestStatus.Approved}");
        output.WriteLine($"Denied:   {(int)AccessRequestStatus.Denied}");
        output.WriteLine($"Expired:  {(int)AccessRequestStatus.Expired}");
        output.WriteLine($"Revoked:  {(int)AccessRequestStatus.Revoked}");

        Assert.Equal(0, (int)AccessRequestStatus.Pending);
        Assert.Equal(1, (int)AccessRequestStatus.Approved);
        Assert.Equal(2, (int)AccessRequestStatus.Denied);
        Assert.Equal(3, (int)AccessRequestStatus.Expired);
        Assert.Equal(4, (int)AccessRequestStatus.Revoked);
    }

    // ── Revocation ───────────────────────────────────────────────────────────

    [Fact]
    public void IsGrantActive_False_WhenRevoked()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };

        output.WriteLine($"Status: Approved, GrantExpiresAt: +4h, RevokedAt: -5m");
        output.WriteLine($"IsGrantActive: {request.IsGrantActive}");
        output.WriteLine($"IsRevoked: {request.IsRevoked}");

        Assert.False(request.IsGrantActive);
        Assert.True(request.IsRevoked);
    }

    [Fact]
    public void IsGrantActive_False_WhenRevokedEvenIfGrantStillValid()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = DateTimeOffset.UtcNow,
        };

        output.WriteLine("Grant valid for 7 more days but revoked => IsGrantActive: false");

        Assert.False(request.IsGrantActive);
    }

    [Fact]
    public void IsRevoked_False_ByDefault()
    {
        var request = new AccessRequest();

        output.WriteLine($"RevokedAt: {request.RevokedAt?.ToString() ?? "null"}");
        output.WriteLine($"IsRevoked: {request.IsRevoked}");

        Assert.False(request.IsRevoked);
        Assert.Null(request.RevokedAt);
        Assert.Null(request.RevokedBy);
        Assert.Null(request.RevokedReason);
    }

    [Fact]
    public void IsRevoked_True_WhenRevokedAtSet()
    {
        var request = new AccessRequest
        {
            RevokedAt = DateTimeOffset.UtcNow,
            RevokedBy = Guid.NewGuid(),
            RevokedReason = "compromised",
        };

        output.WriteLine($"RevokedAt: {request.RevokedAt}");
        output.WriteLine($"RevokedReason: {request.RevokedReason}");
        output.WriteLine($"IsRevoked: {request.IsRevoked}");

        Assert.True(request.IsRevoked);
    }
}
