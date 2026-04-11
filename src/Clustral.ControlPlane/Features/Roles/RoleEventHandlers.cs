using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Infrastructure;
using MassTransit;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Roles;

public sealed class RoleAuditHandler(
    ILogger<RoleAuditHandler> logger,
    IPublishEndpoint publisher)
    : INotificationHandler<RoleCreated>,
      INotificationHandler<RoleUpdated>,
      INotificationHandler<RoleDeleted>
{
    public async Task Handle(RoleCreated e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} created: {Name} by {Actor}",
            e.RoleId, e.Name, e.ActorEmail ?? "unknown");

        await publisher.Publish(new RoleCreatedEvent
        {
            RoleId = e.RoleId,
            Name = e.Name,
            CreatedByEmail = e.ActorEmail,
            KubernetesGroups = e.KubernetesGroups,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(RoleUpdated e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} updated by {Actor}",
            e.RoleId, e.ActorEmail ?? "unknown");

        await publisher.Publish(new RoleUpdatedEvent
        {
            RoleId = e.RoleId,
            Name = e.Name,
            Description = e.Description,
            UpdatedByEmail = e.ActorEmail,
            KubernetesGroups = e.KubernetesGroups,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(RoleDeleted e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} ({Name}) deleted by {Actor}",
            e.RoleId, e.RoleName ?? "unknown", e.ActorEmail ?? "unknown");

        await publisher.Publish(new RoleDeletedEvent
        {
            RoleId = e.RoleId,
            Name = e.RoleName,
            DeletedByEmail = e.ActorEmail,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
