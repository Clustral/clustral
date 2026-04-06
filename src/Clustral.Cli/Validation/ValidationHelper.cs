using System.CommandLine.Invocation;
using Clustral.Cli.Ui;
using FluentValidation;
using Spectre.Console;

namespace Clustral.Cli.Validation;

internal static class ValidationHelper
{
    /// <summary>
    /// Validates the input and writes a styled error card if invalid.
    /// Returns <c>true</c> if valid; sets <c>ctx.ExitCode = 1</c> and returns
    /// <c>false</c> if invalid.
    /// </summary>
    internal static bool Validate<T>(
        IAnsiConsole console,
        AbstractValidator<T> validator,
        T input,
        InvocationContext ctx)
    {
        var result = validator.Validate(input);
        if (result.IsValid) return true;

        CliErrors.WriteValidationErrors(console, result.Errors);
        ctx.ExitCode = 1;
        return false;
    }
}
