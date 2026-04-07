using MongoDB.Bson.Serialization.Attributes;

namespace Clustral.ControlPlane.Domain.Events;

/// <summary>
/// Mixin interface for entities that raise domain events. Events are
/// collected in-memory and dispatched after persistence by the handler.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>
/// Base implementation for domain event collection. Entities inherit
/// from this or implement <see cref="IHasDomainEvents"/> directly.
/// </summary>
public abstract class HasDomainEvents : IHasDomainEvents
{
    [BsonIgnore]
    private readonly List<IDomainEvent> _domainEvents = [];

    [BsonIgnore]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void RaiseDomainEvent(IDomainEvent @event) => _domainEvents.Add(@event);
}
