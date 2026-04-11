namespace Clustral.ControlPlane.Domain.Repositories;

public interface IRoleAssignmentRepository
{
    Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<RoleAssignment>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task InsertAsync(RoleAssignment assignment, CancellationToken ct = default);
    Task DeleteByUserAndClusterAsync(Guid userId, Guid clusterId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task DeleteByRoleIdAsync(Guid roleId, CancellationToken ct = default);
}
