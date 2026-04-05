using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Api.Controllers;

[ApiController]
[Route("api/v1/roles")]
[Authorize]
public sealed class RolesController(ClustralDb db, ILogger<RolesController> logger)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var roles = await db.Roles
            .Find(FilterDefinition<Role>.Empty)
            .SortBy(r => r.Name)
            .ToListAsync(ct);

        var response = roles.Select(r => new RoleResponse(
            r.Id, r.Name, r.Description, r.KubernetesGroups, r.CreatedAt)).ToList();

        return Ok(new RoleListResponse(response));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var exists = await db.Roles.Find(r => r.Name == request.Name).AnyAsync(ct);
        if (exists)
            return Conflict(new { error = $"Role '{request.Name}' already exists." });

        var role = new Role
        {
            Id               = Guid.NewGuid(),
            Name             = request.Name,
            Description      = request.Description,
            KubernetesGroups = request.KubernetesGroups ?? [],
        };

        await db.Roles.InsertOneAsync(role, cancellationToken: ct);
        logger.LogInformation("Role {Name} created with id {Id}", role.Name, role.Id);

        return CreatedAtAction(nameof(List), new RoleResponse(
            role.Id, role.Name, role.Description, role.KubernetesGroups, role.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var update = Builders<Role>.Update.Combine();
        if (request.Name is not null)
            update = update.Set(r => r.Name, request.Name);
        if (request.Description is not null)
            update = update.Set(r => r.Description, request.Description);
        if (request.KubernetesGroups is not null)
            update = update.Set(r => r.KubernetesGroups, request.KubernetesGroups);

        var result = await db.Roles.UpdateOneAsync(r => r.Id == id, update, cancellationToken: ct);
        if (result.MatchedCount == 0)
            return NotFound();

        var role = await db.Roles.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        return Ok(new RoleResponse(role!.Id, role.Name, role.Description, role.KubernetesGroups, role.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await db.Roles.DeleteOneAsync(r => r.Id == id, ct);
        if (result.DeletedCount == 0)
            return NotFound();

        // Cascade: remove all assignments for this role.
        await db.RoleAssignments.DeleteManyAsync(a => a.RoleId == id, ct);
        logger.LogInformation("Role {RoleId} deleted", id);

        return NoContent();
    }
}
