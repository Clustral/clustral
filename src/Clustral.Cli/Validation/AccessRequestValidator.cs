using FluentValidation;

namespace Clustral.Cli.Validation;

internal sealed class AccessRequestValidator : AbstractValidator<AccessRequestInput>
{
    public AccessRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required (--role).");

        RuleFor(x => x.Cluster)
            .NotEmpty().WithMessage("Cluster is required (--cluster).");

        RuleFor(x => x.Duration)
            .Must(Iso8601Duration.IsValid)
            .WithMessage("Duration must be a valid ISO 8601 duration (e.g., PT8H, P1D).")
            .When(x => x.Duration is not null);
    }
}
