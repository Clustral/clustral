using Clustral.ControlPlane.Infrastructure.Auth;
using FluentValidation;

namespace Clustral.ControlPlane.Features.Shared;

public sealed class OidcOptionsValidator : AbstractValidator<OidcOptions>
{
    public OidcOptionsValidator()
    {
        RuleFor(x => x.Authority)
            .NotEmpty()
            .WithMessage("Oidc:Authority is required.")
            .Must(BeAValidUrl)
            .WithMessage("Oidc:Authority must be a valid absolute URL.");

        RuleFor(x => x.ClientId)
            .NotEmpty()
            .WithMessage("Oidc:ClientId is required.");

        RuleFor(x => x.MetadataAddress)
            .Must(BeAValidUrl)
            .When(x => !string.IsNullOrEmpty(x.MetadataAddress))
            .WithMessage("Oidc:MetadataAddress must be a valid absolute URL when specified.");

        RuleFor(x => x.DefaultKubeconfigCredentialTtl)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("Oidc:DefaultKubeconfigCredentialTtl must be greater than zero.");

        RuleFor(x => x.MaxKubeconfigCredentialTtl)
            .GreaterThanOrEqualTo(x => x.DefaultKubeconfigCredentialTtl)
            .WithMessage("Oidc:MaxKubeconfigCredentialTtl must be >= DefaultKubeconfigCredentialTtl.");
    }

    private static bool BeAValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}
