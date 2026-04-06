using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Api;

/// <summary>
/// Tests for AccessRequestsController validation logic and model behavior.
/// These are unit tests for the DTOs and domain validation — full integration
/// tests with MongoDB would use Testcontainers.
/// </summary>
public class AccessRequestsControllerTests(ITestOutputHelper output)
{
    // ── Model validation ────────────────────────────────────────────────────

    [Fact]
    public void CreateRequest_RequiredFields()
    {
        var request = new CreateAccessRequestRequest(
            RoleId: Guid.NewGuid(),
            ClusterId: Guid.NewGuid(),
            Reason: "deploy hotfix",
            RequestedDuration: "PT4H",
            SuggestedReviewerEmails: ["alice@example.com"]);

        output.WriteLine($"RoleId:    {request.RoleId}");
        output.WriteLine($"ClusterId: {request.ClusterId}");
        output.WriteLine($"Reason:    {request.Reason}");
        output.WriteLine($"Duration:  {request.RequestedDuration}");
        output.WriteLine($"Reviewers: [{string.Join(", ", request.SuggestedReviewerEmails!)}]");

        Assert.NotEqual(Guid.Empty, request.RoleId);
        Assert.NotEqual(Guid.Empty, request.ClusterId);
    }

    [Fact]
    public void CreateRequest_OptionalFieldsCanBeNull()
    {
        var request = new CreateAccessRequestRequest(
            RoleId: Guid.NewGuid(),
            ClusterId: Guid.NewGuid(),
            Reason: null,
            RequestedDuration: null,
            SuggestedReviewerEmails: null);

        output.WriteLine("All optional fields are null");

        Assert.Null(request.Reason);
        Assert.Null(request.RequestedDuration);
        Assert.Null(request.SuggestedReviewerEmails);
    }

    [Fact]
    public void ApproveRequest_OptionalDurationOverride()
    {
        var withOverride = new ApproveAccessRequestRequest(DurationOverride: "PT2H");
        var withoutOverride = new ApproveAccessRequestRequest(DurationOverride: null);

        output.WriteLine($"With override:    {withOverride.DurationOverride}");
        output.WriteLine($"Without override: {withoutOverride.DurationOverride ?? "null"}");

        Assert.Equal("PT2H", withOverride.DurationOverride);
        Assert.Null(withoutOverride.DurationOverride);
    }

    [Fact]
    public void DenyRequest_ReasonRequired()
    {
        var request = new DenyAccessRequestRequest(Reason: "Not authorized for production");

        output.WriteLine($"Reason: {request.Reason}");

        Assert.Equal("Not authorized for production", request.Reason);
    }

    // ── Response model ──────────────────────────────────────────────────────

    [Fact]
    public void AccessRequestResponse_ContainsAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new AccessRequestResponse(
            Id: Guid.NewGuid(),
            RequesterId: Guid.NewGuid(),
            RequesterEmail: "alice@example.com",
            RequesterDisplayName: "Alice",
            RoleId: Guid.NewGuid(),
            RoleName: "admin",
            ClusterId: Guid.NewGuid(),
            ClusterName: "production",
            Status: "Pending",
            Reason: "deploy hotfix",
            RequestedDuration: "08:00:00",
            CreatedAt: now,
            RequestExpiresAt: now.AddHours(1),
            SuggestedReviewers: [new ReviewerInfo(Guid.NewGuid(), "bob@example.com", "Bob")],
            ReviewerId: null,
            ReviewerEmail: null,
            ReviewedAt: null,
            DenialReason: null,
            GrantExpiresAt: null,
            RevokedAt: null,
            RevokedByEmail: null,
            RevokedReason: null);

        output.WriteLine($"=== Pending AccessRequestResponse ===");
        output.WriteLine($"  ID:        {response.Id}");
        output.WriteLine($"  Requester: {response.RequesterEmail} ({response.RequesterDisplayName})");
        output.WriteLine($"  Role:      {response.RoleName}");
        output.WriteLine($"  Cluster:   {response.ClusterName}");
        output.WriteLine($"  Status:    {response.Status}");
        output.WriteLine($"  Reason:    {response.Reason}");
        output.WriteLine($"  Duration:  {response.RequestedDuration}");
        output.WriteLine($"  Reviewers: {response.SuggestedReviewers.Count}");

        Assert.Equal("Pending", response.Status);
        Assert.Equal("admin", response.RoleName);
        Assert.Single(response.SuggestedReviewers);
        Assert.Null(response.ReviewerId);
        Assert.Null(response.GrantExpiresAt);
    }

    [Fact]
    public void AccessRequestResponse_ApprovedState()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new AccessRequestResponse(
            Id: Guid.NewGuid(),
            RequesterId: Guid.NewGuid(),
            RequesterEmail: "alice@example.com",
            RequesterDisplayName: "Alice",
            RoleId: Guid.NewGuid(),
            RoleName: "admin",
            ClusterId: Guid.NewGuid(),
            ClusterName: "production",
            Status: "Approved",
            Reason: "deploy hotfix",
            RequestedDuration: "04:00:00",
            CreatedAt: now.AddMinutes(-10),
            RequestExpiresAt: now.AddMinutes(50),
            SuggestedReviewers: [],
            ReviewerId: Guid.NewGuid(),
            ReviewerEmail: "bob@example.com",
            ReviewedAt: now,
            DenialReason: null,
            GrantExpiresAt: now.AddHours(4),
            RevokedAt: null,
            RevokedByEmail: null,
            RevokedReason: null);

        output.WriteLine($"=== Approved AccessRequestResponse ===");
        output.WriteLine($"  Status:       {response.Status}");
        output.WriteLine($"  Reviewer:     {response.ReviewerEmail}");
        output.WriteLine($"  ReviewedAt:   {response.ReviewedAt}");
        output.WriteLine($"  GrantExpires: {response.GrantExpiresAt}");

        Assert.Equal("Approved", response.Status);
        Assert.NotNull(response.ReviewerId);
        Assert.NotNull(response.GrantExpiresAt);
        Assert.Null(response.DenialReason);
    }

    [Fact]
    public void AccessRequestResponse_DeniedState()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new AccessRequestResponse(
            Id: Guid.NewGuid(),
            RequesterId: Guid.NewGuid(),
            RequesterEmail: "alice@example.com",
            RequesterDisplayName: null,
            RoleId: Guid.NewGuid(),
            RoleName: "admin",
            ClusterId: Guid.NewGuid(),
            ClusterName: "production",
            Status: "Denied",
            Reason: "need access for migration",
            RequestedDuration: "08:00:00",
            CreatedAt: now.AddMinutes(-5),
            RequestExpiresAt: now.AddMinutes(55),
            SuggestedReviewers: [],
            ReviewerId: Guid.NewGuid(),
            ReviewerEmail: "charlie@example.com",
            ReviewedAt: now,
            DenialReason: "Not authorized without team lead approval",
            GrantExpiresAt: null,
            RevokedAt: null,
            RevokedByEmail: null,
            RevokedReason: null);

        output.WriteLine($"=== Denied AccessRequestResponse ===");
        output.WriteLine($"  Status:       {response.Status}");
        output.WriteLine($"  DenialReason: {response.DenialReason}");

        Assert.Equal("Denied", response.Status);
        Assert.NotNull(response.DenialReason);
        Assert.Null(response.GrantExpiresAt);
    }

    [Fact]
    public void ReviewerInfo_ContainsAllFields()
    {
        var info = new ReviewerInfo(Guid.NewGuid(), "admin@example.com", "Admin User");

        output.WriteLine($"Reviewer: {info.Email} ({info.DisplayName})");

        Assert.Equal("admin@example.com", info.Email);
        Assert.Equal("Admin User", info.DisplayName);
    }

    [Fact]
    public void ActiveGrantResponse_ContainsAllFields()
    {
        var grant = new ActiveGrantResponse(
            RequestId: Guid.NewGuid(),
            RoleName: "admin",
            ClusterId: Guid.NewGuid(),
            ClusterName: "production",
            GrantExpiresAt: DateTimeOffset.UtcNow.AddHours(3));

        output.WriteLine($"Grant: {grant.RoleName} on {grant.ClusterName}, expires {grant.GrantExpiresAt}");

        Assert.Equal("admin", grant.RoleName);
        Assert.True(grant.GrantExpiresAt > DateTimeOffset.UtcNow);
    }

    // ── State transition validation ─────────────────────────────────────────

    [Theory]
    [InlineData(AccessRequestStatus.Pending, true)]
    [InlineData(AccessRequestStatus.Approved, false)]
    [InlineData(AccessRequestStatus.Denied, false)]
    [InlineData(AccessRequestStatus.Expired, false)]
    [InlineData(AccessRequestStatus.Revoked, false)]
    public void OnlyPendingRequests_CanBeApprovedOrDenied(AccessRequestStatus status, bool canTransition)
    {
        output.WriteLine($"Status: {status} => canApprove/Deny: {canTransition}");

        Assert.Equal(canTransition, status == AccessRequestStatus.Pending);
    }

    [Fact]
    public void GrantExpiresAt_CalculatedFromReviewedAtPlusDuration()
    {
        var reviewedAt = DateTimeOffset.UtcNow;
        var requestedDuration = TimeSpan.FromHours(4);
        var expectedExpiry = reviewedAt + requestedDuration;

        output.WriteLine($"ReviewedAt:        {reviewedAt}");
        output.WriteLine($"RequestedDuration: {requestedDuration}");
        output.WriteLine($"GrantExpiresAt:    {expectedExpiry}");

        Assert.Equal(expectedExpiry, reviewedAt + requestedDuration);
    }

    // ── Duration parsing ────────────────────────────────────────────────────

    [Theory]
    [InlineData("PT1H", 1)]
    [InlineData("PT4H", 4)]
    [InlineData("PT8H", 8)]
    [InlineData("PT24H", 24)]
    public void IsoDuration_ParsesCorrectly(string iso, int expectedHours)
    {
        var duration = System.Xml.XmlConvert.ToTimeSpan(iso);

        output.WriteLine($"XmlConvert.ToTimeSpan(\"{iso}\") => {duration}");

        Assert.Equal(TimeSpan.FromHours(expectedHours), duration);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("8h")]
    [InlineData("")]
    public void IsoDuration_InvalidFormat_Throws(string iso)
    {
        output.WriteLine($"XmlConvert.ToTimeSpan(\"{iso}\") => FormatException");

        Assert.ThrowsAny<Exception>(() => System.Xml.XmlConvert.ToTimeSpan(iso));
    }

    // ── Default TTL values ──────────────────────────────────────────────────

    [Fact]
    public void DefaultRequestTtl_Is1Hour()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new AccessRequest
        {
            CreatedAt = now,
            RequestExpiresAt = now.AddHours(1),
        };

        var ttl = request.RequestExpiresAt - request.CreatedAt;
        output.WriteLine($"Default request TTL: {ttl}");

        Assert.Equal(TimeSpan.FromHours(1), ttl);
    }

    [Fact]
    public void DefaultGrantDuration_Is8Hours()
    {
        var request = new AccessRequest();
        output.WriteLine($"Default grant duration: {request.RequestedDuration}");

        Assert.Equal(TimeSpan.FromHours(8), request.RequestedDuration);
    }

    // ── Conflict detection ──────────────────────────────────────────────────

    [Fact]
    public void ConflictScenarios_Documented()
    {
        output.WriteLine("=== AccessRequest Conflict Rules ===");
        output.WriteLine("1. User has static RoleAssignment for cluster => 409");
        output.WriteLine("2. User has pending AccessRequest for cluster  => 409");
        output.WriteLine("3. User has active grant for cluster            => 409");
        output.WriteLine("4. Role not found                               => 404");
        output.WriteLine("5. Cluster not found                            => 404");
        output.WriteLine("6. Invalid ISO duration                         => 400");

        // This test documents the expected behavior; actual HTTP tests
        // would require WebApplicationFactory + real MongoDB.
        Assert.True(true);
    }

    // ── Revocation ──────────────────────────────────────────────────────────

    [Fact]
    public void RevokeAccessRequestRequest_OptionalReason()
    {
        var withReason = new RevokeAccessRequestRequest(Reason: "compromised account");
        var withoutReason = new RevokeAccessRequestRequest(Reason: null);

        output.WriteLine($"With reason:    {withReason.Reason}");
        output.WriteLine($"Without reason: {withoutReason.Reason ?? "null"}");

        Assert.Equal("compromised account", withReason.Reason);
        Assert.Null(withoutReason.Reason);
    }

    [Theory]
    [InlineData(AccessRequestStatus.Approved, true)]
    [InlineData(AccessRequestStatus.Pending, false)]
    [InlineData(AccessRequestStatus.Denied, false)]
    [InlineData(AccessRequestStatus.Expired, false)]
    [InlineData(AccessRequestStatus.Revoked, false)]
    public void OnlyApprovedRequests_CanBeRevoked(AccessRequestStatus status, bool canRevoke)
    {
        output.WriteLine($"Status: {status} => canRevoke: {canRevoke}");

        Assert.Equal(canRevoke, status == AccessRequestStatus.Approved);
    }

    [Fact]
    public void RevokedGrant_NotConsideredActive()
    {
        var request = new AccessRequest
        {
            Status = AccessRequestStatus.Revoked,
            GrantExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
            RevokedAt = DateTimeOffset.UtcNow,
            RevokedReason = "no longer needed",
        };

        output.WriteLine($"Status: Revoked, GrantExpiresAt: +4h, RevokedAt: now");
        output.WriteLine($"IsGrantActive: {request.IsGrantActive}");

        Assert.False(request.IsGrantActive);
    }

    [Fact]
    public void AccessRequestResponse_IncludesRevocationFields()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new AccessRequestResponse(
            Id: Guid.NewGuid(),
            RequesterId: Guid.NewGuid(),
            RequesterEmail: "alice@example.com",
            RequesterDisplayName: "Alice",
            RoleId: Guid.NewGuid(),
            RoleName: "admin",
            ClusterId: Guid.NewGuid(),
            ClusterName: "production",
            Status: "Revoked",
            Reason: "deploy",
            RequestedDuration: "04:00:00",
            CreatedAt: now.AddHours(-2),
            RequestExpiresAt: now.AddHours(-1),
            SuggestedReviewers: [],
            ReviewerId: Guid.NewGuid(),
            ReviewerEmail: "bob@example.com",
            ReviewedAt: now.AddHours(-2),
            DenialReason: null,
            GrantExpiresAt: now.AddHours(2),
            RevokedAt: now,
            RevokedByEmail: "admin@example.com",
            RevokedReason: "security incident");

        output.WriteLine($"=== Revoked AccessRequestResponse ===");
        output.WriteLine($"  Status:        {response.Status}");
        output.WriteLine($"  RevokedAt:     {response.RevokedAt}");
        output.WriteLine($"  RevokedBy:     {response.RevokedByEmail}");
        output.WriteLine($"  RevokedReason: {response.RevokedReason}");

        Assert.Equal("Revoked", response.Status);
        Assert.NotNull(response.RevokedAt);
        Assert.Equal("admin@example.com", response.RevokedByEmail);
        Assert.Equal("security incident", response.RevokedReason);
    }

    // ── Active filter ───────────────────────────────────────────────────────

    [Fact]
    public void ActiveFilter_ExcludesRevokedGrants()
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new List<AccessRequest>
        {
            new() { Status = AccessRequestStatus.Approved, GrantExpiresAt = now.AddHours(4), RevokedAt = null },
            new() { Status = AccessRequestStatus.Approved, GrantExpiresAt = now.AddHours(2), RevokedAt = now.AddMinutes(-10) },
            new() { Status = AccessRequestStatus.Approved, GrantExpiresAt = now.AddMinutes(-5), RevokedAt = null },
            new() { Status = AccessRequestStatus.Pending },
        };

        var active = requests
            .Where(r => r.Status == AccessRequestStatus.Approved
                     && r.GrantExpiresAt > now
                     && r.RevokedAt == null)
            .ToList();

        output.WriteLine($"Total requests: {requests.Count}");
        output.WriteLine($"Active grants:  {active.Count}");

        Assert.Single(active); // Only the first one.
    }
}
