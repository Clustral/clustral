using Clustral.ControlPlane.Infrastructure.Auth;
using FluentValidation;

namespace Clustral.ControlPlane.Features.Shared;

public sealed class CredentialOptionsValidator : AbstractValidator<CredentialOptions>
{
    public CredentialOptionsValidator()
    {
        RuleFor(x => x.DefaultKubeconfigCredentialTtl)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("Credential:DefaultKubeconfigCredentialTtl must be greater than zero.");

        RuleFor(x => x.MaxKubeconfigCredentialTtl)
            .GreaterThanOrEqualTo(x => x.DefaultKubeconfigCredentialTtl)
            .WithMessage("Credential:MaxKubeconfigCredentialTtl must be >= DefaultKubeconfigCredentialTtl.");
    }
}
