using FluentValidation;

namespace Clustral.Cli.Validation;

internal sealed class AccessDenyValidator : AbstractValidator<AccessDenyInput>
{
    public AccessDenyValidator()
    {
        RuleFor(x => x.RequestId)
            .NotEmpty().WithMessage("Request ID is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("Request ID must be a valid GUID.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required when denying a request (--reason).");
    }
}
