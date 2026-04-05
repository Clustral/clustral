using System.ComponentModel.DataAnnotations;

namespace Clustral.ControlPlane.Api.Models;

public sealed record CreateRoleRequest(
    [Required] string Name,
    string Description = "",
    List<string>? KubernetesGroups = null);

public sealed record UpdateRoleRequest(
    string? Name = null,
    string? Description = null,
    List<string>? KubernetesGroups = null);

public sealed record RoleResponse(
    Guid           Id,
    string         Name,
    string         Description,
    List<string>   KubernetesGroups,
    DateTimeOffset CreatedAt);

public sealed record RoleListResponse(
    IReadOnlyList<RoleResponse> Roles);
