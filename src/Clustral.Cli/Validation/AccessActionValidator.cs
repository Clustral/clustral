using FluentValidation;

namespace Clustral.Cli.Validation;

internal sealed class AccessActionValidator : AbstractValidator<AccessActionInput>
{
    public AccessActionValidator()
    {
        RuleFor(x => x.RequestId)
            .NotEmpty().WithMessage("Request ID is required.")
            .Must(id => Guid.TryParse(id, out _)).WithMessage("Request ID must be a valid GUID.");
    }
}
