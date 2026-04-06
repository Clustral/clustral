using FluentValidation;

namespace Clustral.Cli.Validation;

internal sealed class KubeLoginValidator : AbstractValidator<KubeLoginInput>
{
    public KubeLoginValidator()
    {
        RuleFor(x => x.ClusterId)
            .NotEmpty().WithMessage("Cluster ID is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("Cluster ID must be a valid GUID.");

        RuleFor(x => x.Ttl)
            .Must(Iso8601Duration.IsValid)
            .WithMessage("TTL must be a valid ISO 8601 duration (e.g., PT8H, P1D).")
            .When(x => x.Ttl is not null);
    }
}
