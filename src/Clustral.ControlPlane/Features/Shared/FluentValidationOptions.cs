using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clustral.ControlPlane.Features.Shared;

/// <summary>
/// Adapts FluentValidation validators to the <see cref="IValidateOptions{TOptions}"/>
/// interface so they run automatically via <c>.ValidateOnStart()</c>.
/// Based on: https://andrewlock.net/adding-validation-to-strongly-typed-configuration-objects-using-flentvalidation/
/// </summary>
public sealed class FluentValidationOptions<TOptions>(
    string? optionsName,
    IServiceProvider serviceProvider)
    : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        // Named options support — skip if name doesn't match.
        if (optionsName is not null && optionsName != name)
            return ValidateOptionsResult.Skip;

        ArgumentNullException.ThrowIfNull(options);

        using var scope = serviceProvider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<TOptions>>();
        var results = validator.Validate(options);

        if (results.IsValid)
            return ValidateOptionsResult.Success;

        var errors = results.Errors
            .Select(e => $"Configuration validation failed for '{typeof(TOptions).Name}.{e.PropertyName}': {e.ErrorMessage}")
            .ToList();

        return ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// Extension methods for registering options with FluentValidation + ValidateOnStart.
/// </summary>
public static class FluentValidationOptionsExtensions
{
    /// <summary>
    /// Adds FluentValidation-based validation to an <see cref="OptionsBuilder{TOptions}"/>.
    /// Chain with <c>.ValidateOnStart()</c> to fail fast at startup.
    /// </summary>
    public static OptionsBuilder<TOptions> ValidateFluentValidation<TOptions>(
        this OptionsBuilder<TOptions> optionsBuilder)
        where TOptions : class
    {
        optionsBuilder.Services.AddSingleton<IValidateOptions<TOptions>>(
            provider => new FluentValidationOptions<TOptions>(
                optionsBuilder.Name, provider));
        return optionsBuilder;
    }

    /// <summary>
    /// Registers options with configuration binding, FluentValidation, and ValidateOnStart
    /// in a single call. Requires a corresponding <see cref="IValidator{T}"/> to be registered.
    /// </summary>
    public static OptionsBuilder<TOptions> AddOptionsWithValidation<TOptions, TValidator>(
        this IServiceCollection services,
        string configurationSection)
        where TOptions : class
        where TValidator : class, IValidator<TOptions>
    {
        services.AddScoped<IValidator<TOptions>, TValidator>();

        return services.AddOptions<TOptions>()
            .BindConfiguration(configurationSection)
            .ValidateFluentValidation()
            .ValidateOnStart();
    }
}
