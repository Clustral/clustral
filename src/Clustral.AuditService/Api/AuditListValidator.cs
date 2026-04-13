using FluentValidation;

namespace Clustral.AuditService.Api;

/// <summary>
/// Query-parameter validator for <c>GET /api/v1/audit</c>. Replaces ad-hoc
/// <c>BadRequest("...")</c> checks in <see cref="Controllers.AuditController"/>
/// so validation failures flow through the <see cref="Sdk.Results.Result{T}"/>
/// pattern and produce RFC 7807 bodies consistent with the rest of the app.
/// </summary>
public sealed class AuditListValidator : AbstractValidator<AuditListQuery>
{
    public AuditListValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be >= 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 200)
            .WithMessage("PageSize must be between 1 and 200.");
    }
}

/// <summary>Query-parameter record matching <c>AuditController.List</c> inputs
/// that need validation.</summary>
public sealed record AuditListQuery(int Page, int PageSize);
