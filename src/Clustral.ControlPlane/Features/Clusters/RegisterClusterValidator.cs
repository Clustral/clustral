using FluentValidation;

namespace Clustral.ControlPlane.Features.Clusters;

public sealed class RegisterClusterValidator : AbstractValidator<RegisterClusterCommand>
{
    public RegisterClusterValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Cluster name is required.")
            .MaximumLength(100).WithMessage("Cluster name must be 100 characters or fewer.");
    }
}
