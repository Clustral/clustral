using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure.Repositories;

public sealed class MongoRoleRepository(ClustralDb db) : IRoleRepository
{
    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Roles.Find(r => r.Id == id).FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct) =>
        await db.Roles.Find(r => r.Name == name).AnyAsync(ct);

    public async Task<List<Role>> ListAsync(CancellationToken ct) =>
        await db.Roles.Find(_ => true).ToListAsync(ct);

    public async Task InsertAsync(Role role, CancellationToken ct) =>
        await db.Roles.InsertOneAsync(role, cancellationToken: ct);

    public async Task ReplaceAsync(Role role, CancellationToken ct) =>
        await db.Roles.ReplaceOneAsync(r => r.Id == role.Id, role, cancellationToken: ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var result = await db.Roles.DeleteOneAsync(r => r.Id == id, ct);
        return result.DeletedCount > 0;
    }
}
