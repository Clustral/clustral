using System.ComponentModel.DataAnnotations;

namespace Clustral.ControlPlane.Api.Models;

public sealed record CreateAccessRequestRequest(
    [Required] Guid RoleId,
    [Required] Guid ClusterId,
    string? Reason,
    string? RequestedDuration,
    List<string>? SuggestedReviewerEmails);

public sealed record ApproveAccessRequestRequest(
    string? DurationOverride);

public sealed record DenyAccessRequestRequest(
    [Required] string Reason);

public sealed record AccessRequestResponse(
    Guid            Id,
    Guid            RequesterId,
    string          RequesterEmail,
    string?         RequesterDisplayName,
    Guid            RoleId,
    string          RoleName,
    Guid            ClusterId,
    string          ClusterName,
    string          Status,
    string          Reason,
    string          RequestedDuration,
    DateTimeOffset  CreatedAt,
    DateTimeOffset  RequestExpiresAt,
    IReadOnlyList<ReviewerInfo> SuggestedReviewers,
    Guid?           ReviewerId,
    string?         ReviewerEmail,
    DateTimeOffset? ReviewedAt,
    string?         DenialReason,
    DateTimeOffset? GrantExpiresAt);

public sealed record ReviewerInfo(
    Guid    Id,
    string  Email,
    string? DisplayName);

public sealed record AccessRequestListResponse(
    IReadOnlyList<AccessRequestResponse> Requests);

public sealed record ActiveGrantResponse(
    Guid           RequestId,
    string         RoleName,
    Guid           ClusterId,
    string         ClusterName,
    DateTimeOffset GrantExpiresAt);
