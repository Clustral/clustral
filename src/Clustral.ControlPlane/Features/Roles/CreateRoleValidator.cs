using Clustral.ControlPlane.Features.Roles.Commands;
using FluentValidation;

namespace Clustral.ControlPlane.Features.Roles;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(100).WithMessage("Role name must be 100 characters or fewer.");
    }
}
