using MediatR;

namespace Clustral.ControlPlane.Domain.Events;

/// <summary>
/// Marker interface for domain events. Extends MediatR's INotification
/// so events can be dispatched via the existing MediatR pipeline.
/// </summary>
public interface IDomainEvent : INotification
{
    DateTimeOffset OccurredAt { get; }
}
