using Clustral.ControlPlane.Infrastructure;
using FluentValidation;

namespace Clustral.ControlPlane.Features.Proxy;

public sealed class ProxyOptionsValidator : AbstractValidator<ProxyOptions>
{
    public ProxyOptionsValidator()
    {
        RuleFor(x => x.TunnelTimeout)
            .GreaterThanOrEqualTo(TimeSpan.FromSeconds(10))
            .WithMessage("Proxy:TunnelTimeout must be at least 10 seconds.")
            .LessThanOrEqualTo(TimeSpan.FromMinutes(10))
            .WithMessage("Proxy:TunnelTimeout must not exceed 10 minutes.");

        When(x => x.RateLimiting.Enabled, () =>
        {
            RuleFor(x => x.RateLimiting.BurstSize)
                .GreaterThan(0)
                .WithMessage("Proxy:RateLimiting:BurstSize must be greater than 0.");

            RuleFor(x => x.RateLimiting.RequestsPerSecond)
                .GreaterThan(0)
                .WithMessage("Proxy:RateLimiting:RequestsPerSecond must be greater than 0.");

            RuleFor(x => x.RateLimiting.QueueSize)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Proxy:RateLimiting:QueueSize must be 0 or greater.");

            RuleFor(x => x.RateLimiting.BurstSize)
                .GreaterThanOrEqualTo(x => x.RateLimiting.RequestsPerSecond)
                .WithMessage("Proxy:RateLimiting:BurstSize must be >= RequestsPerSecond.");
        });
    }
}
