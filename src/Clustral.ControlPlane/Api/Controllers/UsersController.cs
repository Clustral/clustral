using System.Security.Claims;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(ClustralDb db, ILogger<UsersController> logger)
    : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var subject = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                   ?? User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

        if (string.IsNullOrEmpty(subject))
            return Unauthorized();

        var user = await db.Users
            .Find(u => u.KeycloakSubject == subject)
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return NotFound(new { error = "User not found. This is your first API call — try again." });

        var assignments = await db.RoleAssignments
            .Find(a => a.UserId == user.Id)
            .ToListAsync(ct);

        var roleIds    = assignments.Select(a => a.RoleId).Distinct().ToList();
        var clusterIds = assignments.Select(a => a.ClusterId).Distinct().ToList();

        var roles    = (await db.Roles.Find(r => roleIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id);
        var clusters = (await db.Clusters.Find(c => clusterIds.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);

        var assignmentResponses = assignments.Select(a => new RoleAssignmentResponse(
            a.Id, a.UserId, a.RoleId,
            roles.GetValueOrDefault(a.RoleId)?.Name ?? "unknown",
            a.ClusterId,
            clusters.GetValueOrDefault(a.ClusterId)?.Name ?? "unknown",
            a.AssignedAt, a.AssignedBy)).ToList();

        // Fetch active JIT grants.
        var now = DateTimeOffset.UtcNow;
        var activeRequests = await db.AccessRequests
            .Find(r => r.RequesterId == user.Id
                     && r.Status == Domain.AccessRequestStatus.Approved
                     && r.GrantExpiresAt > now
                     && r.RevokedAt == null)
            .ToListAsync(ct);

        var grantClusterIds = activeRequests.Select(r => r.ClusterId).Distinct().ToList();
        var grantRoleIds    = activeRequests.Select(r => r.RoleId).Distinct().ToList();
        var grantClusters   = (await db.Clusters.Find(c => grantClusterIds.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
        var grantRoles      = (await db.Roles.Find(r => grantRoleIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id);

        var activeGrants = activeRequests.Select(r => new ActiveGrantResponse(
            r.Id,
            grantRoles.GetValueOrDefault(r.RoleId)?.Name ?? "unknown",
            r.ClusterId,
            grantClusters.GetValueOrDefault(r.ClusterId)?.Name ?? "unknown",
            r.GrantExpiresAt!.Value)).ToList();

        return Ok(new UserProfileResponse(
            user.Id,
            user.Email ?? user.KeycloakSubject,
            user.DisplayName,
            user.CreatedAt,
            user.LastSeenAt,
            assignmentResponses,
            activeGrants));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var users = await db.Users
            .Find(FilterDefinition<User>.Empty)
            .SortBy(u => u.Email)
            .ToListAsync(ct);

        var response = users.Select(u => new UserResponse(
            u.Id, u.Email ?? u.KeycloakSubject, u.DisplayName, u.CreatedAt, u.LastSeenAt)).ToList();

        return Ok(new UserListResponse(response));
    }

    [HttpGet("{id:guid}/assignments")]
    public async Task<IActionResult> GetAssignments(Guid id, CancellationToken ct)
    {
        var assignments = await db.RoleAssignments
            .Find(a => a.UserId == id)
            .ToListAsync(ct);

        // Enrich with role and cluster names.
        var roleIds    = assignments.Select(a => a.RoleId).Distinct().ToList();
        var clusterIds = assignments.Select(a => a.ClusterId).Distinct().ToList();

        var roles = await db.Roles
            .Find(r => roleIds.Contains(r.Id))
            .ToListAsync(ct);
        var clusters = await db.Clusters
            .Find(c => clusterIds.Contains(c.Id))
            .ToListAsync(ct);

        var roleMap    = roles.ToDictionary(r => r.Id, r => r.Name);
        var clusterMap = clusters.ToDictionary(c => c.Id, c => c.Name);

        var response = assignments.Select(a => new RoleAssignmentResponse(
            a.Id,
            a.UserId,
            a.RoleId,
            roleMap.GetValueOrDefault(a.RoleId, "unknown"),
            a.ClusterId,
            clusterMap.GetValueOrDefault(a.ClusterId, "unknown"),
            a.AssignedAt,
            a.AssignedBy)).ToList();

        return Ok(new RoleAssignmentListResponse(response));
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> AssignRole(
        Guid id,
        [FromBody] AssignRoleRequest request,
        CancellationToken ct)
    {
        // Verify user, role, and cluster exist.
        var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { error = "User not found." });

        var role = await db.Roles.Find(r => r.Id == request.RoleId).FirstOrDefaultAsync(ct);
        if (role is null) return NotFound(new { error = "Role not found." });

        var cluster = await db.Clusters.Find(c => c.Id == request.ClusterId).FirstOrDefaultAsync(ct);
        if (cluster is null) return NotFound(new { error = "Cluster not found." });

        // Upsert — replace existing assignment for this user+cluster.
        var callerEmail = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
                       ?? User.Claims.FirstOrDefault(c => c.Type == "email")?.Value
                       ?? "unknown";

        var assignment = new RoleAssignment
        {
            Id         = Guid.NewGuid(),
            UserId     = id,
            RoleId     = request.RoleId,
            ClusterId  = request.ClusterId,
            AssignedBy = callerEmail,
        };

        // Delete existing assignment for this user+cluster, then insert new one.
        await db.RoleAssignments.DeleteManyAsync(
            a => a.UserId == id && a.ClusterId == request.ClusterId, ct);
        await db.RoleAssignments.InsertOneAsync(assignment, cancellationToken: ct);

        logger.LogInformation(
            "Assigned role {RoleName} to user {Email} on cluster {ClusterName}",
            role.Name, user.Email, cluster.Name);

        return CreatedAtAction(nameof(GetAssignments), new { id },
            new RoleAssignmentResponse(
                assignment.Id, id, role.Id, role.Name,
                cluster.Id, cluster.Name, assignment.AssignedAt, assignment.AssignedBy));
    }

    [HttpDelete("{userId:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> RemoveAssignment(Guid userId, Guid assignmentId, CancellationToken ct)
    {
        var result = await db.RoleAssignments.DeleteOneAsync(
            a => a.Id == assignmentId && a.UserId == userId, ct);

        if (result.DeletedCount == 0) return NotFound();

        logger.LogInformation("Removed role assignment {Id} for user {UserId}", assignmentId, userId);
        return NoContent();
    }
}
