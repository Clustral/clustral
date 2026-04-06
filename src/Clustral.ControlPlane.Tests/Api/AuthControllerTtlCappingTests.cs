using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

/// <summary>
/// Tests for credential TTL capping logic when user has a JIT grant.
/// Tests the decision logic, not the HTTP layer.
/// </summary>
public class AuthControllerTtlCappingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Simulates the TTL capping logic from AuthController (lines 106-122).
    /// </summary>
    private static DateTimeOffset CalculateCredentialExpiry(
        DateTimeOffset now,
        TimeSpan requestedTtl,
        RoleAssignment? staticAssignment,
        AccessRequest? activeGrant)
    {
        var expiresAt = now + requestedTtl;

        if (staticAssignment is null &&
            activeGrant is not null &&
            activeGrant.IsGrantActive &&
            activeGrant.GrantExpiresAt.HasValue &&
            activeGrant.GrantExpiresAt.Value < expiresAt)
        {
            expiresAt = activeGrant.GrantExpiresAt.Value;
        }

        return expiresAt;
    }

    [Fact]
    public void StaticAssignment_NormalTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var requestedTtl = TimeSpan.FromHours(8);
        var assignment = new RoleAssignment { RoleId = Guid.NewGuid() };

        var expiry = CalculateCredentialExpiry(now, requestedTtl, assignment, activeGrant: null);

        output.WriteLine($"Requested TTL: {requestedTtl}");
        output.WriteLine($"Credential expires: {expiry} (not capped)");

        Assert.Equal(now + requestedTtl, expiry);
    }

    [Fact]
    public void JitGrant_CapsCredentialTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var requestedTtl = TimeSpan.FromHours(8);
        var grantExpiry = now.AddHours(2);

        var grant = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = grantExpiry,
        };

        var expiry = CalculateCredentialExpiry(now, requestedTtl, staticAssignment: null, grant);

        output.WriteLine($"Requested TTL:  {requestedTtl}");
        output.WriteLine($"Grant expires:  {grantExpiry}");
        output.WriteLine($"Credential:     {expiry} (capped to grant)");

        Assert.Equal(grantExpiry, expiry);
    }

    [Fact]
    public void JitGrant_LongerThanTtl_UsesRequestedTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var requestedTtl = TimeSpan.FromHours(2);
        var grantExpiry = now.AddHours(8);

        var grant = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = grantExpiry,
        };

        var expiry = CalculateCredentialExpiry(now, requestedTtl, staticAssignment: null, grant);

        output.WriteLine($"Requested TTL:  {requestedTtl}");
        output.WriteLine($"Grant expires:  {grantExpiry}");
        output.WriteLine($"Credential:     {expiry} (uses requested, shorter)");

        Assert.Equal(now + requestedTtl, expiry);
    }

    [Fact]
    public void NoAssignment_NoGrant_NormalTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var requestedTtl = TimeSpan.FromHours(8);

        var expiry = CalculateCredentialExpiry(now, requestedTtl, null, null);

        output.WriteLine("No assignment, no grant => normal TTL");

        Assert.Equal(now + requestedTtl, expiry);
    }

    [Fact]
    public void ExpiredGrant_NormalTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var requestedTtl = TimeSpan.FromHours(8);

        var grant = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = now.AddMinutes(-5), // Expired 5 min ago
        };

        var expiry = CalculateCredentialExpiry(now, requestedTtl, null, grant);

        output.WriteLine($"Grant expired 5m ago => IsGrantActive: {grant.IsGrantActive}");
        output.WriteLine($"Credential: {expiry} (not capped — grant invalid)");

        Assert.Equal(now + requestedTtl, expiry);
    }

    [Fact]
    public void StaticAssignment_IgnoresGrant()
    {
        var now = DateTimeOffset.UtcNow;
        var requestedTtl = TimeSpan.FromHours(8);
        var assignment = new RoleAssignment { RoleId = Guid.NewGuid() };
        var grant = new AccessRequest
        {
            Status = AccessRequestStatus.Approved,
            GrantExpiresAt = now.AddHours(1), // Would cap if checked
        };

        var expiry = CalculateCredentialExpiry(now, requestedTtl, assignment, grant);

        output.WriteLine("Static assignment present => grant ignored");
        output.WriteLine($"Credential: {expiry} (full 8h)");

        Assert.Equal(now + requestedTtl, expiry);
    }
}
