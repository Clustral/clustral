using Clustral.Sdk.Results;
using MediatR;

namespace Clustral.Sdk.Cqs;

/// <summary>
/// Marker interface for commands (write operations) that return a result.
/// Commands modify state and are validated by <c>ValidationBehavior</c>.
/// </summary>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>
/// Marker interface for commands that return a non-generic <see cref="Result"/>.
/// </summary>
public interface ICommand : IRequest<Result>;

/// <summary>
/// Marker interface for queries (read operations).
/// Queries do not modify state and skip validation.
/// </summary>
public interface IQuery<TResponse> : IRequest<TResponse>;
