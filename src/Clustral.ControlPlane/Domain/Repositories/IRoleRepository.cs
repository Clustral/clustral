namespace Clustral.ControlPlane.Domain.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    Task<List<Role>> ListAsync(CancellationToken ct = default);
    Task InsertAsync(Role role, CancellationToken ct = default);
    Task ReplaceAsync(Role role, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
