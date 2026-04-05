using Clustral.Agent;
using Clustral.Agent.Proxy;
using Clustral.Agent.Tunnel;
using Clustral.Agent.Worker;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

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
