using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Domain.Specifications;
using Clustral.Sdk.Results;

namespace Clustral.ControlPlane.Domain.Services;

/// <summary>
/// Resolves the impersonation identity for a user on a cluster.
/// Checks static role assignment first, then falls back to active JIT grant.
/// Extracted from KubectlProxyMiddleware for testability and DDD alignment.
/// </summary>
public sealed class ImpersonationResolver(
    IUserRepository users,
    IRoleRepository roles,
    AccessSpecifications specs)
{
    /// <summary>
    /// Resolves the user + groups to impersonate on the k8s API server.
    /// </summary>
    public async Task<Result<ImpersonationIdentity>> ResolveAsync(
        Guid userId, Guid clusterId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null)
            return ResultErrors.UserNotFound();

        var impersonateUser = user.Email ?? user.KeycloakSubject;

        // Resolve role: static assignment takes precedence over JIT grant.
        Guid roleId;
        var assignment = await specs.GetStaticAssignmentAsync(userId, clusterId, ct);
        if (assignment is not null)
        {
            roleId = assignment.RoleId;
        }
        else
        {
            var grant = await specs.GetActiveGrantAsync(userId, clusterId, ct);
            if (grant is null)
            {
                return ResultError.Forbidden(
                    "No role assigned for this cluster. Request access with 'clustral access request'.");
            }
            roleId = grant.RoleId;
        }

        // Build impersonation groups from the role.
        var groups = new List<string> { "system:authenticated" };
        var role = await roles.GetByIdAsync(roleId, ct);
        if (role is not null)
        {
            foreach (var group in role.KubernetesGroups)
            {
                if (!groups.Contains(group))
                    groups.Add(group);
            }
        }

        return new ImpersonationIdentity(impersonateUser, groups.AsReadOnly());
    }
}
