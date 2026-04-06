using System.Security.Claims;
using System.Xml;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Clustral.Sdk.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/v1/access-requests")]
[Authorize]
public sealed class AccessRequestsController(ClustralDb db, ILogger<AccessRequestsController> logger)
    : ControllerBase
{
    private static readonly TimeSpan DefaultRequestTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultGrantDuration = TimeSpan.FromHours(8);

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/access-requests
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccessRequestRequest request,
        CancellationToken ct)
    {
        var user = await ResolveCurrentUserAsync(ct);
        if (user is null) return ResultErrors.UserUnauthorized().ToActionResult();

        // Validate role and cluster exist.
        var role = await db.Roles.Find(r => r.Id == request.RoleId).FirstOrDefaultAsync(ct);
        if (role is null) return ResultErrors.RoleNotFound(request.RoleId.ToString()).ToActionResult();

        var cluster = await db.Clusters.Find(c => c.Id == request.ClusterId).FirstOrDefaultAsync(ct);
        if (cluster is null) return ResultErrors.ClusterNotFound(request.ClusterId.ToString()).ToActionResult();

        // Check user doesn't already have static access.
        var existingAssignment = await db.RoleAssignments
            .Find(a => a.UserId == user.Id && a.ClusterId == request.ClusterId)
            .FirstOrDefaultAsync(ct);
        if (existingAssignment is not null)
            return ResultErrors.StaticAssignmentExists().ToActionResult();

        // Check no duplicate pending request.
        var existingPending = await db.AccessRequests
            .Find(r => r.RequesterId == user.Id
                     && r.ClusterId == request.ClusterId
                     && r.Status == AccessRequestStatus.Pending)
            .FirstOrDefaultAsync(ct);
        if (existingPending is not null)
            return ResultErrors.PendingRequestExists(existingPending.Id).ToActionResult();

        // Check no active grant already exists.
        var now = DateTimeOffset.UtcNow;
        var existingGrant = await db.AccessRequests
            .Find(r => r.RequesterId == user.Id
                     && r.ClusterId == request.ClusterId
                     && r.Status == AccessRequestStatus.Approved
                     && r.GrantExpiresAt > now)
            .FirstOrDefaultAsync(ct);
        if (existingGrant is not null)
            return ResultErrors.GrantAlreadyActive(existingGrant.Id).ToActionResult();

        // Parse duration.
        var duration = DefaultGrantDuration;
        if (!string.IsNullOrEmpty(request.RequestedDuration))
        {
            try { duration = XmlConvert.ToTimeSpan(request.RequestedDuration); }
            catch { return ResultErrors.InvalidDuration(request.RequestedDuration).ToActionResult(); }
        }

        // Resolve suggested reviewer emails to user IDs.
        var suggestedReviewerIds = new List<Guid>();
        if (request.SuggestedReviewerEmails is { Count: > 0 })
        {
            foreach (var email in request.SuggestedReviewerEmails)
            {
                var reviewer = await db.Users
                    .Find(u => u.Email == email)
                    .FirstOrDefaultAsync(ct);
                if (reviewer is not null)
                    suggestedReviewerIds.Add(reviewer.Id);
            }
        }

        var accessRequest = new AccessRequest
        {
            Id                = Guid.NewGuid(),
            RequesterId       = user.Id,
            RoleId            = request.RoleId,
            ClusterId         = request.ClusterId,
            Reason            = request.Reason ?? string.Empty,
            RequestedDuration = duration,
            RequestExpiresAt  = now + DefaultRequestTtl,
            SuggestedReviewers = suggestedReviewerIds,
        };

        await db.AccessRequests.InsertOneAsync(accessRequest, cancellationToken: ct);

        logger.LogInformation(
            "Access request {RequestId} created by {Email} for role {Role} on cluster {Cluster}",
            accessRequest.Id, user.Email, role.Name, cluster.Name);

        return CreatedAtAction(nameof(Get), new { id = accessRequest.Id },
            await EnrichAsync(accessRequest, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/access-requests
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] bool mine,
        [FromQuery] bool active,
        CancellationToken ct)
    {
        var builder = Builders<AccessRequest>.Filter;
        var filter = builder.Empty;

        if (active)
        {
            // Active grants: approved, not revoked, not yet expired.
            var now = DateTimeOffset.UtcNow;
            filter &= builder.Eq(r => r.Status, AccessRequestStatus.Approved)
                    & builder.Gt(r => r.GrantExpiresAt, now)
                    & builder.Eq(r => r.RevokedAt, null);
        }
        else if (!string.IsNullOrEmpty(status) && Enum.TryParse<AccessRequestStatus>(status, true, out var s))
        {
            filter &= builder.Eq(r => r.Status, s);
        }

        if (mine)
        {
            var user = await ResolveCurrentUserAsync(ct);
            if (user is null) return ResultErrors.UserUnauthorized().ToActionResult();
            filter &= builder.Eq(r => r.RequesterId, user.Id);
        }

        var requests = await db.AccessRequests
            .Find(filter)
            .SortByDescending(r => r.CreatedAt)
            .Limit(100)
            .ToListAsync(ct);

        var enriched = new List<AccessRequestResponse>();
        foreach (var r in requests)
            enriched.Add(await EnrichAsync(r, ct));

        return Ok(new AccessRequestListResponse(enriched));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/v1/access-requests/{id}
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var request = await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (request is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.").ToActionResult();

        return Ok(await EnrichAsync(request, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/access-requests/{id}/approve
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody] ApproveAccessRequestRequest? body,
        CancellationToken ct)
    {
        var reviewer = await ResolveCurrentUserAsync(ct);
        if (reviewer is null) return ResultErrors.UserUnauthorized().ToActionResult();

        var request = await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (request is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.").ToActionResult();

        if (request.Status != AccessRequestStatus.Pending)
            return ResultErrors.RequestNotPending(request.Status.ToString()).ToActionResult();

        if (request.IsPendingExpired)
            return ResultErrors.RequestExpired().ToActionResult();

        var now = DateTimeOffset.UtcNow;
        var grantDuration = request.RequestedDuration;

        if (!string.IsNullOrEmpty(body?.DurationOverride))
        {
            try { grantDuration = XmlConvert.ToTimeSpan(body.DurationOverride); }
            catch { return BadRequest(new { error = "Invalid duration override." }); }
        }

        var update = Builders<AccessRequest>.Update
            .Set(r => r.Status, AccessRequestStatus.Approved)
            .Set(r => r.ReviewerId, reviewer.Id)
            .Set(r => r.ReviewedAt, now)
            .Set(r => r.GrantExpiresAt, now + grantDuration);

        await db.AccessRequests.UpdateOneAsync(r => r.Id == id, update, cancellationToken: ct);

        logger.LogInformation(
            "Access request {RequestId} approved by {Reviewer}, grant expires at {Expiry}",
            id, reviewer.Email, now + grantDuration);

        var updated = await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        return Ok(await EnrichAsync(updated!, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/access-requests/{id}/deny
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/deny")]
    public async Task<IActionResult> Deny(
        Guid id,
        [FromBody] DenyAccessRequestRequest body,
        CancellationToken ct)
    {
        var reviewer = await ResolveCurrentUserAsync(ct);
        if (reviewer is null) return ResultErrors.UserUnauthorized().ToActionResult();

        var request = await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (request is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.").ToActionResult();

        if (request.Status != AccessRequestStatus.Pending)
            return ResultErrors.RequestNotPending(request.Status.ToString()).ToActionResult();

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessRequest>.Update
            .Set(r => r.Status, AccessRequestStatus.Denied)
            .Set(r => r.ReviewerId, reviewer.Id)
            .Set(r => r.ReviewedAt, now)
            .Set(r => r.DenialReason, body.Reason);

        await db.AccessRequests.UpdateOneAsync(r => r.Id == id, update, cancellationToken: ct);

        logger.LogInformation(
            "Access request {RequestId} denied by {Reviewer}: {Reason}",
            id, reviewer.Email, body.Reason);

        var updated = await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        return Ok(await EnrichAsync(updated!, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/v1/access-requests/{id}/revoke
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(
        Guid id,
        [FromBody] RevokeAccessRequestRequest? body,
        CancellationToken ct)
    {
        var revoker = await ResolveCurrentUserAsync(ct);
        if (revoker is null) return ResultErrors.UserUnauthorized().ToActionResult();

        var request = await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        if (request is null) return ResultError.NotFound("REQUEST_NOT_FOUND", "Access request not found.").ToActionResult();

        if (request.Status != AccessRequestStatus.Approved)
            return ResultErrors.GrantNotApproved(request.Status.ToString()).ToActionResult();

        if (request.IsRevoked)
            return ResultErrors.GrantAlreadyRevoked().ToActionResult();

        if (!request.IsGrantActive)
            return ResultErrors.GrantAlreadyExpired().ToActionResult();

        var now = DateTimeOffset.UtcNow;
        var update = Builders<AccessRequest>.Update
            .Set(r => r.Status, AccessRequestStatus.Revoked)
            .Set(r => r.RevokedAt, now)
            .Set(r => r.RevokedBy, revoker.Id)
            .Set(r => r.RevokedReason, body?.Reason);

        await db.AccessRequests.UpdateOneAsync(r => r.Id == id, update, cancellationToken: ct);

        logger.LogInformation(
            "Access request {RequestId} revoked by {Revoker}: {Reason}",
            id, revoker.Email, body?.Reason ?? "(no reason)");

        var updated = await db.AccessRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        return Ok(await EnrichAsync(updated!, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<User?> ResolveCurrentUserAsync(CancellationToken ct)
    {
        var subject = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                   ?? User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

        if (string.IsNullOrEmpty(subject)) return null;

        return await db.Users
            .Find(u => u.KeycloakSubject == subject)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<AccessRequestResponse> EnrichAsync(AccessRequest r, CancellationToken ct)
    {
        var requester = await db.Users.Find(u => u.Id == r.RequesterId).FirstOrDefaultAsync(ct);
        var role      = await db.Roles.Find(rl => rl.Id == r.RoleId).FirstOrDefaultAsync(ct);
        var cluster   = await db.Clusters.Find(c => c.Id == r.ClusterId).FirstOrDefaultAsync(ct);

        string? reviewerEmail = null;
        if (r.ReviewerId.HasValue)
        {
            var reviewer = await db.Users.Find(u => u.Id == r.ReviewerId.Value).FirstOrDefaultAsync(ct);
            reviewerEmail = reviewer?.Email ?? reviewer?.KeycloakSubject;
        }

        // Resolve suggested reviewer info.
        var reviewerInfos = new List<ReviewerInfo>();
        foreach (var reviewerId in r.SuggestedReviewers)
        {
            var u = await db.Users.Find(u => u.Id == reviewerId).FirstOrDefaultAsync(ct);
            if (u is not null)
                reviewerInfos.Add(new ReviewerInfo(u.Id, u.Email ?? u.KeycloakSubject, u.DisplayName));
        }

        string? revokedByEmail = null;
        if (r.RevokedBy.HasValue)
        {
            var revoker = await db.Users.Find(u => u.Id == r.RevokedBy.Value).FirstOrDefaultAsync(ct);
            revokedByEmail = revoker?.Email ?? revoker?.KeycloakSubject;
        }

        return new AccessRequestResponse(
            r.Id,
            r.RequesterId,
            requester?.Email ?? requester?.KeycloakSubject ?? "unknown",
            requester?.DisplayName,
            r.RoleId,
            role?.Name ?? "unknown",
            r.ClusterId,
            cluster?.Name ?? "unknown",
            r.Status.ToString(),
            r.Reason,
            r.RequestedDuration.ToString(),
            r.CreatedAt,
            r.RequestExpiresAt,
            reviewerInfos,
            r.ReviewerId,
            reviewerEmail,
            r.ReviewedAt,
            r.DenialReason,
            r.GrantExpiresAt,
            r.RevokedAt,
            revokedByEmail,
            r.RevokedReason);
    }
}
