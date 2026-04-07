using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Infrastructure.Repositories;

public sealed class MongoUserRepository(ClustralDb db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);

    public async Task<User?> GetBySubjectAsync(string subject, CancellationToken ct) =>
        await db.Users.Find(u => u.KeycloakSubject == subject).FirstOrDefaultAsync(ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        await db.Users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);

    public async Task<List<User>> ListAsync(CancellationToken ct) =>
        await db.Users.Find(_ => true).ToListAsync(ct);

    public async Task InsertAsync(User user, CancellationToken ct) =>
        await db.Users.InsertOneAsync(user, cancellationToken: ct);

    public async Task ReplaceAsync(User user, CancellationToken ct) =>
        await db.Users.ReplaceOneAsync(u => u.Id == user.Id, user, cancellationToken: ct);
}
