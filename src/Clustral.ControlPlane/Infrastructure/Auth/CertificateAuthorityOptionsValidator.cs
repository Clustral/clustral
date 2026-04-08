using Clustral.Sdk.Crypto;
using FluentValidation;

namespace Clustral.ControlPlane.Infrastructure.Auth;

public sealed class CertificateAuthorityOptionsValidator : AbstractValidator<CertificateAuthorityOptions>
{
    public CertificateAuthorityOptionsValidator()
    {
        RuleFor(x => x.CaCertPath)
            .NotEmpty()
            .WithMessage("CertificateAuthority:CaCertPath is required")
            .Must(File.Exists)
            .When(x => !string.IsNullOrEmpty(x.CaCertPath))
            .WithMessage(x => $"CA certificate file not found: {x.CaCertPath}");

        RuleFor(x => x.CaKeyPath)
            .NotEmpty()
            .WithMessage("CertificateAuthority:CaKeyPath is required")
            .Must(File.Exists)
            .When(x => !string.IsNullOrEmpty(x.CaKeyPath))
            .WithMessage(x => $"CA key file not found: {x.CaKeyPath}");

        RuleFor(x => x.ClientCertValidityDays)
            .GreaterThan(0)
            .LessThanOrEqualTo(3650);

        RuleFor(x => x.JwtValidityDays)
            .GreaterThan(0)
            .LessThanOrEqualTo(365);
    }
}
