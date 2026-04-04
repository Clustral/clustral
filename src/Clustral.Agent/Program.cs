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
// HttpClient for Kubernetes API proxying
// ─────────────────────────────────────────────────────────────────────────────

// Register as a named client so the factory can be tested / replaced easily.
builder.Services.AddHttpClient("kubernetes", (sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(opts.KubernetesApiUrl);
    client.Timeout     = TimeSpan.FromSeconds(300);   // long-running kubectl exec / log watches
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    // KubectlProxy.CreateKubernetesHttpClient builds the right handler
    // (in-cluster SA token, CA cert, or TLS skip for dev).
    var tempClient = KubectlProxy.CreateKubernetesHttpClient(
        opts.KubernetesApiUrl, opts.KubernetesSkipTlsVerify);
    // Extract the underlying handler from the temp client.
    // We need the handler, not the client itself, because AddHttpClient wraps it.
    // Workaround: use a forwarding handler that delegates to a fixed HttpClient.
    return new ForwardingHandler(tempClient);
});

// ─────────────────────────────────────────────────────────────────────────────
// Agent services
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<AgentCredentialStore>();

builder.Services.AddSingleton<KubectlProxy>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger  = sp.GetRequiredService<ILogger<KubectlProxy>>();
    return new KubectlProxy(factory.CreateClient("kubernetes"), logger);
});

builder.Services.AddSingleton<TunnelManager>();
builder.Services.AddHostedService<AgentWorker>();

// ─────────────────────────────────────────────────────────────────────────────
// Build and run
// ─────────────────────────────────────────────────────────────────────────────

var host = builder.Build();
await host.RunAsync();

// ─────────────────────────────────────────────────────────────────────────────
// ForwardingHandler — internal helper that proxies calls to a pre-built
// HttpClient so we can reuse the in-cluster setup logic in KubectlProxy.
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class ForwardingHandler : HttpMessageHandler
{
    private readonly HttpClient _client;
    internal ForwardingHandler(HttpClient client) { _client = client; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
}
