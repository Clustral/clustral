// Delegate to shared SDK CQS interfaces.
// ControlPlane code keeps using Clustral.ControlPlane.Features.Shared namespace.

namespace Clustral.ControlPlane.Features.Shared;

/// <inheritdoc cref="Clustral.Sdk.Cqs.ICommand{TResponse}"/>
public interface ICommand<TResponse> : Clustral.Sdk.Cqs.ICommand<TResponse>;

/// <inheritdoc cref="Clustral.Sdk.Cqs.ICommand"/>
public interface ICommand : Clustral.Sdk.Cqs.ICommand;

/// <inheritdoc cref="Clustral.Sdk.Cqs.IQuery{TResponse}"/>
public interface IQuery<TResponse> : Clustral.Sdk.Cqs.IQuery<TResponse>;
