using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure.Repositories;

public sealed class MongoRoleAssignmentRepository(ClustralDb db) : IRoleAssignmentRepository
{
    public async Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.RoleAssignments.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<RoleAssignment>> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.RoleAssignments.Find(a => a.UserId == userId).ToListAsync(ct);

    public async Task InsertAsync(RoleAssignment assignment, CancellationToken ct) =>
        await db.RoleAssignments.InsertOneAsync(assignment, cancellationToken: ct);

    public async Task DeleteByUserAndClusterAsync(Guid userId, Guid clusterId, CancellationToken ct) =>
        await db.RoleAssignments.DeleteManyAsync(
            a => a.UserId == userId && a.ClusterId == clusterId, ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await db.RoleAssignments.DeleteOneAsync(a => a.Id == id, ct);
        return result.DeletedCount > 0;
    }

    public async Task DeleteByRoleIdAsync(Guid roleId, CancellationToken ct) =>
        await db.RoleAssignments.DeleteManyAsync(a => a.RoleId == roleId, ct);
}
