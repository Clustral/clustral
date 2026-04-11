using MassTransit.Logging;
using MassTransit.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Clustral.Sdk.Telemetry;

/// <summary>
/// Shared OpenTelemetry registration for metrics + distributed tracing.
/// Instruments ASP.NET Core, HttpClient, and MassTransit automatically.
/// Exports to an OTLP-compatible collector (Grafana Tempo, Jaeger, etc.).
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Registers OpenTelemetry with metrics (Prometheus-compatible) and
    /// tracing (OTLP export to Tempo). MassTransit's
    /// <see cref="DiagnosticHeaders.DefaultListenerName"/> and
    /// <see cref="InstrumentationOptions.MeterName"/> are included so
    /// message publish/consume operations appear in traces and metrics.
    /// </summary>
    public static IServiceCollection AddApplicationOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OpenTelemetryOptions>()
            .BindConfiguration(OpenTelemetryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration
            .GetRequiredSection(OpenTelemetryOptions.SectionName)
            .Get<OpenTelemetryOptions>()
            ?? throw new InvalidOperationException(
                $"Missing '{OpenTelemetryOptions.SectionName}' configuration section.");

        services
            .AddOpenTelemetry()
            .ConfigureResource(builder =>
            {
                builder.AddService(
                    options.ServiceName,
                    serviceInstanceId: Environment.MachineName);
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(InstrumentationOptions.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                builder.AddOtlpExporter(o =>
                    o.Endpoint = new Uri(options.OtlpEndpoint));
            })
            .WithTracing(builder =>
            {
                builder
                    .AddSource(DiagnosticHeaders.DefaultListenerName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                builder.AddOtlpExporter(o =>
                    o.Endpoint = new Uri(options.OtlpEndpoint));
            });

        return services;
    }
}
