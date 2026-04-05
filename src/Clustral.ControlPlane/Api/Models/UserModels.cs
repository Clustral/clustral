using System.ComponentModel.DataAnnotations;

namespace Clustral.ControlPlane.Api.Models;

public sealed record UserResponse(
    Guid            Id,
    string          Email,
    string?         DisplayName,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? LastSeenAt);

public sealed record UserListResponse(
    IReadOnlyList<UserResponse> Users);

public sealed record AssignRoleRequest(
    [Required] Guid RoleId,
    [Required] Guid ClusterId);

public sealed record RoleAssignmentResponse(
    Guid           Id,
    Guid           UserId,
    Guid           RoleId,
    string         RoleName,
    Guid           ClusterId,
    string         ClusterName,
    DateTimeOffset AssignedAt,
    string         AssignedBy);

public sealed record RoleAssignmentListResponse(
    IReadOnlyList<RoleAssignmentResponse> Assignments);
