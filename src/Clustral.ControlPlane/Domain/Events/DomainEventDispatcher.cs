using MediatR;

namespace Clustral.ControlPlane.Domain.Events;

/// <summary>
/// Dispatches domain events collected on an entity via MediatR.
/// Call after persisting the entity to ensure events are only published
/// when the state change is committed.
/// </summary>
public static class DomainEventDispatcher
{
    /// <summary>
    /// Publishes all pending domain events on the entity, then clears them.
    /// </summary>
    public static async Task DispatchDomainEventsAsync(
        this IMediator mediator, IHasDomainEvents entity, CancellationToken ct = default)
    {
        var events = entity.DomainEvents.ToList();
        entity.ClearDomainEvents();

        foreach (var @event in events)
            await mediator.Publish(@event, ct);
    }
}
