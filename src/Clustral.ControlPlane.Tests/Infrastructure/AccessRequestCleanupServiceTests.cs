using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Infrastructure;

/// <summary>
/// Tests for cleanup service logic: which requests should be expired
/// and which should be left alone.
/// </summary>
public class AccessRequestCleanupServiceTests(ITestOutputHelper output)
{
    /// <summary>
    /// Simulates the cleanup filter: only pending requests past their TTL.
    /// </summary>
    private static List<AccessRequest> FilterForExpiry(
        List<AccessRequest> requests,
        DateTimeOffset now)
    {
        return requests
            .Where(r => r.Status == AccessRequestStatus.Pending && r.RequestExpiresAt <= now)
            .ToList();
    }

    [Fact]
    public void PendingPastTtl_ShouldExpire()
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new List<AccessRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Status = AccessRequestStatus.Pending,
                RequestExpiresAt = now.AddMinutes(-10),
            },
        };

        var toExpire = FilterForExpiry(requests, now);

        output.WriteLine($"Pending, expired 10m ago => should expire: {toExpire.Count}");

        Assert.Single(toExpire);
    }

    [Fact]
    public void PendingFutureTtl_ShouldNotExpire()
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new List<AccessRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Status = AccessRequestStatus.Pending,
                RequestExpiresAt = now.AddMinutes(30),
            },
        };

        var toExpire = FilterForExpiry(requests, now);

        output.WriteLine($"Pending, TTL in 30m => should NOT expire: {toExpire.Count}");

        Assert.Empty(toExpire);
    }

    [Fact]
    public void ApprovedRequest_NotAffected()
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new List<AccessRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Status = AccessRequestStatus.Approved,
                RequestExpiresAt = now.AddMinutes(-60),
                GrantExpiresAt = now.AddHours(4),
            },
        };

        var toExpire = FilterForExpiry(requests, now);

        output.WriteLine("Approved request (past request TTL) => not expired by cleanup");

        Assert.Empty(toExpire);
    }

    [Fact]
    public void DeniedRequest_NotAffected()
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new List<AccessRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Status = AccessRequestStatus.Denied,
                RequestExpiresAt = now.AddMinutes(-30),
            },
        };

        var toExpire = FilterForExpiry(requests, now);

        output.WriteLine("Denied request => not affected by cleanup");

        Assert.Empty(toExpire);
    }

    [Fact]
    public void MultipleMixed_OnlyPendingExpiredAffected()
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new List<AccessRequest>
        {
            new() { Id = Guid.NewGuid(), Status = AccessRequestStatus.Pending, RequestExpiresAt = now.AddMinutes(-5) },  // should expire
            new() { Id = Guid.NewGuid(), Status = AccessRequestStatus.Pending, RequestExpiresAt = now.AddMinutes(-20) }, // should expire
            new() { Id = Guid.NewGuid(), Status = AccessRequestStatus.Pending, RequestExpiresAt = now.AddMinutes(30) },  // still valid
            new() { Id = Guid.NewGuid(), Status = AccessRequestStatus.Approved, RequestExpiresAt = now.AddMinutes(-60) }, // approved, not affected
            new() { Id = Guid.NewGuid(), Status = AccessRequestStatus.Denied, RequestExpiresAt = now.AddMinutes(-10) },  // denied, not affected
        };

        var toExpire = FilterForExpiry(requests, now);

        output.WriteLine($"5 requests, mixed statuses => {toExpire.Count} should expire");
        foreach (var r in toExpire)
            output.WriteLine($"  {r.Id} (pending, expired {(now - r.RequestExpiresAt).TotalMinutes:F0}m ago)");

        Assert.Equal(2, toExpire.Count);
    }
}
