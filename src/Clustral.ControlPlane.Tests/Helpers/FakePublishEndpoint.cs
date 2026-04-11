using MassTransit;

namespace Clustral.ControlPlane.Tests.Helpers;

/// <summary>
/// Minimal <see cref="IPublishEndpoint"/> fake that captures published messages
/// for assertion. Only the <c>Publish&lt;T&gt;(T, CancellationToken)</c> overload
/// used by the event handlers is implemented; all other members throw.
/// </summary>
internal sealed class FakePublishEndpoint(List<object> published) : IPublishEndpoint
{
    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        published.Add(message!);
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => throw new NotImplementedException();

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => throw new NotImplementedException();

    public Task Publish(object message, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        => throw new NotImplementedException();

    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => throw new NotImplementedException();

    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => throw new NotImplementedException();

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer)
        => throw new NotImplementedException();
}
