using Clustral.Agent;
using Clustral.Agent.Proxy;
using Clustral.Agent.Tunnel;
using Clustral.Agent.Worker;

var builder = Host.CreateApplicationBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// Options
// ─────────────────────────────────────────────────────────────────────────────

builder.Services
    .AddOptions<AgentOptions>()
    .BindConfiguration(AgentOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ─────────────────────────────────────────────────────────────────────────────
// Agent services
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<AgentCredentialStore>();

builder.Services.AddSingleton<KubectlProxy>(sp =>
{
    var opts   = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<KubectlProxy>>();
    var client = KubectlProxy.CreateKubernetesHttpClient(
        opts.KubernetesApiUrl, opts.KubernetesSkipTlsVerify);
    return new KubectlProxy(client, logger);
});

builder.Services.AddSingleton<TunnelManager>();
builder.Services.AddHostedService<AgentWorker>();

// ─────────────────────────────────────────────────────────────────────────────
// Build and run
// ─────────────────────────────────────────────────────────────────────────────

var host = builder.Build();
await host.RunAsync();
