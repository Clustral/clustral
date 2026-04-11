using Clustral.Sdk.Results;
using FluentValidation;
using MediatR;

namespace Clustral.Sdk.Cqs;

/// <summary>
/// MediatR pipeline behavior that automatically validates command requests
/// using FluentValidation before they reach the handler. Queries skip
/// validation. If validation fails, returns a <see cref="Result{T}"/>
/// failure without calling the handler.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var isCommand = typeof(ICommand).IsAssignableFrom(typeof(TRequest)) ||
            typeof(TRequest).GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
        if (!isCommand) return await next();

        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        var first = failures[0];
        var error = ResultError.Validation(first.ErrorMessage, first.PropertyName);

        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failMethod = typeof(TResponse).GetMethod("Fail",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                [typeof(ResultError)]);
            if (failMethod is not null)
                return (TResponse)failMethod.Invoke(null, [error])!;
        }

        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Fail(error);

        throw new ResultFailureException(error);
    }
}
