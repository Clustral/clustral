using System.ComponentModel.DataAnnotations;

namespace Clustral.Sdk.Messaging;

/// <summary>
/// Typed configuration for the RabbitMQ connection. Bound from the
/// <c>RabbitMQ</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    [Required]
    public string Host { get; set; } = "localhost";

    public ushort Port { get; set; } = 5672;

    public string VHost { get; set; } = "/";

    [Required]
    public string User { get; set; } = "guest";

    [Required]
    public string Pass { get; set; } = "guest";
}
