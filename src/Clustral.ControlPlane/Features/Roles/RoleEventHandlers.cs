using Clustral.Contracts.IntegrationEvents;
using Clustral.ControlPlane.Domain.Events;
using Clustral.ControlPlane.Infrastructure;
using MassTransit;
using MediatR;
using MongoDB.Driver;

namespace Clustral.ControlPlane.Features.Roles;

public sealed class RoleAuditHandler(
    ILogger<RoleAuditHandler> logger,
    IPublishEndpoint publisher,
    ClustralDb db)
    : INotificationHandler<RoleCreated>,
      INotificationHandler<RoleUpdated>,
      INotificationHandler<RoleDeleted>
{
    public async Task Handle(RoleCreated e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} created: {Name}, groups: [{Groups}]",
            e.RoleId, e.Name, string.Join(", ", e.KubernetesGroups));

        await publisher.Publish(new RoleCreatedEvent
        {
            RoleId = e.RoleId,
            Name = e.Name,
            KubernetesGroups = e.KubernetesGroups,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(RoleUpdated e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} updated", e.RoleId);

        await publisher.Publish(new RoleUpdatedEvent
        {
            RoleId = e.RoleId,
            Name = e.Name,
            Description = e.Description,
            KubernetesGroups = e.KubernetesGroups,
            OccurredAt = e.OccurredAt
        }, ct);
    }

    public async Task Handle(RoleDeleted e, CancellationToken ct)
    {
        logger.LogInformation("[Audit] Role {RoleId} ({Name}) deleted",
            e.RoleId, e.RoleName ?? "unknown");

        await publisher.Publish(new RoleDeletedEvent
        {
            RoleId = e.RoleId,
            Name = e.RoleName,
            OccurredAt = e.OccurredAt
        }, ct);
    }
}
