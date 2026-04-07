using Clustral.ControlPlane.Domain;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Domain;

public sealed class AccessRequestAggregateTests(ITestOutputHelper output)
{
    private static AccessRequest CreatePending() =>
        AccessRequest.Create(
            requesterId: Guid.NewGuid(),
            roleId: Guid.NewGuid(),
            clusterId: Guid.NewGuid(),
            reason: "Need access for debugging",
            requestedDuration: TimeSpan.FromHours(8),
            requestTtl: TimeSpan.FromHours(1));

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsPendingStatus()
    {
        var ar = CreatePending();

        output.WriteLine($"Status: {ar.Status}");
        ar.Status.Should().Be(AccessRequestStatus.Pending);
        ar.Id.Should().NotBe(Guid.Empty);
        ar.Reason.Should().Be("Need access for debugging");
        ar.RequestedDuration.Should().Be(TimeSpan.FromHours(8));
        ar.RequestExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_NullReason_DefaultsToEmpty()
    {
        var ar = AccessRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            reason: null, TimeSpan.FromHours(4), TimeSpan.FromHours(1));

        ar.Reason.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithSuggestedReviewers()
    {
        var reviewers = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var ar = AccessRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "test", TimeSpan.FromHours(4), TimeSpan.FromHours(1), reviewers);

        ar.SuggestedReviewers.Should().HaveCount(2);
    }

    // ── Approve ─────────────────────────────────────────────────────────────

    [Fact]
    public void Approve_PendingRequest_Succeeds()
    {
        var ar = CreatePending();
        var reviewerId = Guid.NewGuid();

        var result = ar.Approve(reviewerId, TimeSpan.FromHours(8));

        output.WriteLine($"Status: {ar.Status}, GrantExpires: {ar.GrantExpiresAt}");
        result.IsSuccess.Should().BeTrue();
        ar.Status.Should().Be(AccessRequestStatus.Approved);
        ar.ReviewerId.Should().Be(reviewerId);
        ar.ReviewedAt.Should().NotBeNull();
        ar.GrantExpiresAt.Should().NotBeNull();
        ar.GrantExpiresAt!.Value.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddHours(8), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Approve_AlreadyApproved_Fails()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));

        var result = ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(4));

        output.WriteLine($"Error: {result.Error?.Code}");
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("REQUEST_NOT_PENDING");
    }

    [Fact]
    public void Approve_DeniedRequest_Fails()
    {
        var ar = CreatePending();
        ar.Deny(Guid.NewGuid(), "No");

        var result = ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(4));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("REQUEST_NOT_PENDING");
    }

    [Fact]
    public void Approve_ExpiredRequest_Fails()
    {
        var ar = CreatePending();
        // Manually expire by setting RequestExpiresAt to the past
        ar.RequestExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);

        var result = ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));

        output.WriteLine($"Error: {result.Error?.Code}");
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("REQUEST_EXPIRED");
    }

    // ── Deny ────────────────────────────────────────────────────────────────

    [Fact]
    public void Deny_PendingRequest_Succeeds()
    {
        var ar = CreatePending();
        var reviewerId = Guid.NewGuid();

        var result = ar.Deny(reviewerId, "Not authorized");

        output.WriteLine($"Status: {ar.Status}, Reason: {ar.DenialReason}");
        result.IsSuccess.Should().BeTrue();
        ar.Status.Should().Be(AccessRequestStatus.Denied);
        ar.ReviewerId.Should().Be(reviewerId);
        ar.DenialReason.Should().Be("Not authorized");
    }

    [Fact]
    public void Deny_AlreadyApproved_Fails()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));

        var result = ar.Deny(Guid.NewGuid(), "Too late");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("REQUEST_NOT_PENDING");
    }

    // ── Revoke ──────────────────────────────────────────────────────────────

    [Fact]
    public void Revoke_ApprovedGrant_Succeeds()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));

        var revokerId = Guid.NewGuid();
        var result = ar.Revoke(revokerId, "Security incident");

        output.WriteLine($"Status: {ar.Status}, Reason: {ar.RevokedReason}");
        result.IsSuccess.Should().BeTrue();
        ar.Status.Should().Be(AccessRequestStatus.Revoked);
        ar.RevokedBy.Should().Be(revokerId);
        ar.RevokedReason.Should().Be("Security incident");
        ar.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void Revoke_PendingRequest_Fails()
    {
        var ar = CreatePending();

        var result = ar.Revoke(Guid.NewGuid(), "test");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("GRANT_NOT_APPROVED");
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Fails()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));
        ar.Revoke(Guid.NewGuid(), "first");

        var result = ar.Revoke(Guid.NewGuid(), "second");

        output.WriteLine($"Error: {result.Error?.Code}");
        result.IsFailure.Should().BeTrue();
        // After revocation, status is Revoked so GrantNotApproved fires first
        result.Error!.Code.Should().BeOneOf("GRANT_ALREADY_REVOKED", "GRANT_NOT_APPROVED");
    }

    [Fact]
    public void Revoke_ExpiredGrant_Fails()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));
        // Manually expire
        ar.GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);

        var result = ar.Revoke(Guid.NewGuid(), "test");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("GRANT_ALREADY_EXPIRED");
    }

    [Fact]
    public void Revoke_WithNullReason_Succeeds()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));

        var result = ar.Revoke(Guid.NewGuid(), null);

        result.IsSuccess.Should().BeTrue();
        ar.RevokedReason.Should().BeNull();
    }

    // ── Expire ──────────────────────────────────────────────────────────────

    [Fact]
    public void Expire_PendingPastTtl_SetsExpired()
    {
        var ar = CreatePending();
        ar.RequestExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var result = ar.Expire();

        output.WriteLine($"Status: {ar.Status}");
        result.IsSuccess.Should().BeTrue();
        ar.Status.Should().Be(AccessRequestStatus.Expired);
    }

    [Fact]
    public void Expire_ApprovedPastGrant_SetsExpired()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));
        ar.GrantExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        var result = ar.Expire();

        result.IsSuccess.Should().BeTrue();
        ar.Status.Should().Be(AccessRequestStatus.Expired);
    }

    [Fact]
    public void Expire_PendingNotYetExpired_NoOp()
    {
        var ar = CreatePending();

        var result = ar.Expire();

        result.IsSuccess.Should().BeTrue();
        ar.Status.Should().Be(AccessRequestStatus.Pending);
    }

    [Fact]
    public void Expire_ApprovedStillActive_NoOp()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));

        var result = ar.Expire();

        result.IsSuccess.Should().BeTrue();
        ar.Status.Should().Be(AccessRequestStatus.Approved);
    }

    // ── Computed helpers ─────────────────────────────────────────────────────

    [Fact]
    public void IsGrantActive_ApprovedAndNotExpired_True()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));

        ar.IsGrantActive.Should().BeTrue();
    }

    [Fact]
    public void IsGrantActive_Revoked_False()
    {
        var ar = CreatePending();
        ar.Approve(Guid.NewGuid(), TimeSpan.FromHours(8));
        ar.Revoke(Guid.NewGuid(), "test");

        ar.IsGrantActive.Should().BeFalse();
    }
}
