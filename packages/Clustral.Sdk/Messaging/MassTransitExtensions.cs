using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clustral.Sdk.Messaging;

/// <summary>
/// Shared MassTransit + RabbitMQ registration used by both the ControlPlane
/// (publisher only) and the AuditService (publisher + consumers).
///
/// Features:
/// <list type="bullet">
///   <item><b>InMemoryInboxOutbox</b> — idempotent consumers (no duplicate audit logs on retry)</item>
///   <item><b>Quorum queues</b> — replicated, durable, enterprise-grade</item>
///   <item><b>Auto-discovery</b> — consumers found by assembly scan</item>
///   <item><b>Config-driven</b> — connection from <c>appsettings.json</c> <c>RabbitMQ</c> section</item>
/// </list>
/// </summary>
public static class MassTransitExtensions
{
    /// <summary>
    /// Registers MassTransit with RabbitMQ transport. Pass
    /// <paramref name="consumersAssembly"/> to auto-discover and register
    /// <see cref="IConsumer{T}"/> implementations (AuditService). Omit it
    /// for publish-only services (ControlPlane).
    /// </summary>
    public static IServiceCollection AddMassTransitWithRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly? consumersAssembly = null)
    {
        var options = configuration
            .GetRequiredSection(RabbitMqOptions.SectionName)
            .Get<RabbitMqOptions>()
            ?? throw new InvalidOperationException(
                $"Missing '{RabbitMqOptions.SectionName}' configuration section.");

        services.AddInMemoryInboxOutbox();

        services.AddMassTransit(transit =>
        {
            if (consumersAssembly is not null)
            {
                transit.AddConsumers(consumersAssembly);
                transit.AddInMemoryInboxOutbox();
                transit.AddConfigureEndpointsCallback((_, configurator) =>
                {
                    configurator.ConfigureDefaultErrorTransport();
                    if (configurator is IRabbitMqReceiveEndpointConfigurator rmq)
                        rmq.SetQuorumQueue();
                });
            }

            transit.UsingRabbitMq((context, configurator) =>
            {
                configurator.Host(options.Host, options.Port, options.VHost, settings =>
                {
                    settings.Username(options.User);
                    settings.Password(options.Pass);
                });

                configurator.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
