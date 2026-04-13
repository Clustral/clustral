using Clustral.AuditService.Features.Audit.Queries;
using Clustral.Sdk.Results;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clustral.AuditService.Api.Controllers;

/// <summary>
/// Thin MediatR dispatcher for audit event queries.
/// No business logic — all query logic lives in handlers under
/// <c>Features/Audit/Queries/</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/audit")]
public sealed class AuditController(
    IMediator mediator,
    IValidator<AuditListQuery> listValidator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AuditListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> List(
        [FromQuery] string? category = null,
        [FromQuery] string? code = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? user = null,
        [FromQuery] Guid? clusterId = null,
        [FromQuery] Guid? resourceId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        // Validate via FluentValidation so failures surface as RFC 7807
        // Problem Details, consistent with the rest of the app.
        var validation = await listValidator.ValidateAsync(new AuditListQuery(page, pageSize), ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            return ResultError.Validation(first.ErrorMessage, first.PropertyName).ToActionResult();
        }

        var result = await mediator.Send(new ListAuditEventsQuery(
            category, code, severity, user, clusterId, resourceId,
            from, to, page, pageSize), ct);

        return result.ToActionResult();
    }

    [HttpGet("{uid:guid}")]
    [ProducesResponseType<AuditEventResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid uid, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAuditEventQuery(uid), ct);
        return result.ToActionResult();
    }
}

// ── Response DTOs ────────────────────────────────────────────────────────────

public sealed class AuditListResponse
{
    public List<AuditEventResponse> Events { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class AuditEventResponse
{
    public Guid Uid { get; init; }
    public string Event { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? User { get; init; }
    public Guid? UserId { get; init; }
    public string? ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public string? ResourceName { get; init; }
    public string? ClusterName { get; init; }
    public Guid? ClusterId { get; init; }
    public DateTimeOffset Time { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}
