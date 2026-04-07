namespace Clustral.ControlPlane.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetBySubjectAsync(string subject, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<List<User>> ListAsync(CancellationToken ct = default);
    Task InsertAsync(User user, CancellationToken ct = default);
    Task ReplaceAsync(User user, CancellationToken ct = default);
}
