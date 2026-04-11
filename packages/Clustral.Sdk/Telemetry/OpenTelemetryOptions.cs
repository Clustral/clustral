using System.ComponentModel.DataAnnotations;

namespace Clustral.Sdk.Telemetry;

/// <summary>
/// Typed configuration for OpenTelemetry. Bound from the
/// <c>OpenTelemetry</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// Service name reported to the OTLP collector (e.g.
    /// <c>clustral-controlplane</c>, <c>clustral-audit-service</c>).
    /// </summary>
    [Required]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// OTLP exporter endpoint (e.g. <c>http://tempo:4317</c>).
    /// </summary>
    [Required]
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}
