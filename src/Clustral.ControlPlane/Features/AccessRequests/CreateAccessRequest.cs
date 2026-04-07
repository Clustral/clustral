using System.Xml;
using Clustral.ControlPlane.Api.Models;
using Clustral.ControlPlane.Domain;
using Clustral.ControlPlane.Domain.Repositories;
using Clustral.ControlPlane.Domain.Specifications;
using Clustral.ControlPlane.Features.Shared;
using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.ControlPlane.Features.AccessRequests;

public record CreateAccessRequestCommand(
    Guid RoleId, Guid ClusterId, string? Reason,
    string? RequestedDuration, List<string>? SuggestedReviewerEmails)
    : IRequest<Result<AccessRequestResponse>>;

public sealed class CreateAccessRequestHandler(
    IRoleRepository roles,
    IClusterRepository clusters,
    IUserRepository users,
    IAccessRequestRepository accessRequests,
    ICurrentUserProvider currentUser,
    AccessSpecifications specs,
    AccessRequestEnricher enricher,
    ILogger<CreateAccessRequestHandler> logger)
    : IRequestHandler<CreateAccessRequestCommand, Result<AccessRequestResponse>>
{
    private static readonly TimeSpan DefaultRequestTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultGrantDuration = TimeSpan.FromHours(8);

    public async Task<Result<AccessRequestResponse>> Handle(
        CreateAccessRequestCommand request, CancellationToken ct)
    {
        var user = await currentUser.GetCurrentUserAsync(ct);
        if (user is null) return ResultErrors.UserUnauthorized();

        var role = await roles.GetByIdAsync(request.RoleId, ct);
        if (role is null) return ResultErrors.RoleNotFound(request.RoleId.ToString());

        var cluster = await clusters.GetByIdAsync(request.ClusterId, ct);
        if (cluster is null) return ResultErrors.ClusterNotFound(request.ClusterId.ToString());

        // Conflict checks via specifications.
        if (await specs.HasStaticAssignmentAsync(user.Id, request.ClusterId, ct))
            return ResultErrors.StaticAssignmentExists();

        var existingPending = await specs.GetPendingRequestAsync(user.Id, request.ClusterId, ct);
        if (existingPending is not null) return ResultErrors.PendingRequestExists(existingPending.Id);

        var existingGrant = await specs.GetActiveGrantAsync(user.Id, request.ClusterId, ct);
        if (existingGrant is not null) return ResultErrors.GrantAlreadyActive(existingGrant.Id);

        var duration = DefaultGrantDuration;
        if (!string.IsNullOrEmpty(request.RequestedDuration))
        {
            try { duration = XmlConvert.ToTimeSpan(request.RequestedDuration); }
            catch { return ResultErrors.InvalidDuration(request.RequestedDuration); }
        }

        var suggestedReviewerIds = new List<Guid>();
        if (request.SuggestedReviewerEmails is { Count: > 0 })
        {
            foreach (var email in request.SuggestedReviewerEmails)
            {
                var reviewer = await users.GetByEmailAsync(email, ct);
                if (reviewer is not null) suggestedReviewerIds.Add(reviewer.Id);
            }
        }

        var accessRequest = AccessRequest.Create(
            user.Id, request.RoleId, request.ClusterId,
            request.Reason, duration, DefaultRequestTtl,
            suggestedReviewerIds);

        await accessRequests.InsertAsync(accessRequest, ct);

        logger.LogInformation("Access request {RequestId} created by {Email} for role {Role} on cluster {Cluster}",
            accessRequest.Id, user.Email, role.Name, cluster.Name);

        return await enricher.EnrichAsync(accessRequest, ct);
    }
}
