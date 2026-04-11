using Prometheus;
using System.Reflection;
using System.Text.Json;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Infrastructure.Auth;
using Clustral.Sdk.Messaging;
using Clustral.Sdk.Telemetry;
using Clustral.ControlPlane.Protos;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ─────────────────────────────────────────────────────────────────────────────
// Options — validated at startup via FluentValidation + ValidateOnStart().
// If any required config is missing, the app aborts immediately with a clear error.
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddOptionsWithValidation<OidcOptions, OidcOptionsValidator>(OidcOptions.SectionName);

builder.Services.AddOptionsWithValidation<MongoDbOptions, MongoDbOptionsValidator>(MongoDbOptions.SectionName);

builder.Services.AddOptionsWithValidation<ProxyOptions, ProxyOptionsValidator>(ProxyOptions.SectionName);

builder.Services.AddOptionsWithValidation<Clustral.Sdk.Crypto.CertificateAuthorityOptions,
    CertificateAuthorityOptionsValidator>(Clustral.Sdk.Crypto.CertificateAuthorityOptions.SectionName);

var oidcOpts = builder.Configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();

// ─────────────────────────────────────────────────────────────────────────────
// MongoDB — resolved lazily via IOptions<MongoDbOptions>.
// ValidateOnStart() ensures config is valid before first access.
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<MongoDbOptions>>().Value;
    return new MongoClient(opts.ConnectionString);
});
builder.Services.AddSingleton<ClustralDb>();

// ─────────────────────────────────────────────────────────────────────────────
// Authentication — OIDC (JWT Bearer)
// ─────────────────────────────────────────────────────────────────────────────

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.RequireHttpsMetadata = oidcOpts.RequireHttpsMetadata;
        opts.Audience = string.IsNullOrEmpty(oidcOpts.Audience)
            ? oidcOpts.ClientId
            : oidcOpts.Audience;

        if (!string.IsNullOrEmpty(oidcOpts.MetadataAddress))
        {
            // When MetadataAddress is set, the ControlPlane fetches JWKS from
            // an internal URL but the token issuer varies depending on how the
            // user accessed the OIDC provider (localhost vs LAN IP vs hostname).
            // Set Authority to null and use MetadataAddress only — this
            // prevents the JwtBearer middleware from doing its own issuer
            // validation against the Authority URL.
            opts.Authority = null;
            opts.MetadataAddress = oidcOpts.MetadataAddress;
        }
        else
        {
            opts.Authority = oidcOpts.Authority;
        }

        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false, // issuer varies by access URL
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true, // JWKS key check proves authenticity
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Skip OIDC JWT validation for requests on the mTLS port (:5443).
        // Agent auth on that port is handled by AgentAuthInterceptor, not OIDC.
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Connection.LocalPort == 5443)
                {
                    context.NoResult(); // skip OIDC validation
                }
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// ─────────────────────────────────────────────────────────────────────────────
// MVC Controllers + OpenAPI
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddScoped<Clustral.ControlPlane.Api.UserSyncFilter>();
builder.Services.AddControllers(opts => { opts.Filters.AddService<Clustral.ControlPlane.Api.UserSyncFilter>(); });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.SwaggerDoc("v1", new() { Title = "Clustral Control Plane", Version = "v1" });

    // Wire up the bearer token in the Swagger UI.
    opts.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste an OIDC access token (obtained via `clustral login`).",
    });
    opts.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// Certificate Authority + JWT Issuer (agent mTLS + JWT auth)
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<Clustral.Sdk.Crypto.CertificateAuthority>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<Clustral.Sdk.Crypto.CertificateAuthorityOptions>>().Value;
    return new Clustral.Sdk.Crypto.CertificateAuthority(opts.CaCertPath, opts.CaKeyPath, opts);
});
builder.Services.AddSingleton<Clustral.Sdk.Crypto.JwtIssuer>(sp =>
{
    var ca = sp.GetRequiredService<Clustral.Sdk.Crypto.CertificateAuthority>();
    var opts = sp.GetRequiredService<IOptions<Clustral.Sdk.Crypto.CertificateAuthorityOptions>>().Value;
    return new Clustral.Sdk.Crypto.JwtIssuer(ca, opts);
});
builder.Services.AddMemoryCache();

// ─────────────────────────────────────────────────────────────────────────────
// Kestrel mTLS endpoint (:5443) for agent gRPC
// ─────────────────────────────────────────────────────────────────────────────

builder.WebHost.ConfigureKestrel((ctx, kestrel) =>
{
    var caOpts = ctx.Configuration
        .GetSection(Clustral.Sdk.Crypto.CertificateAuthorityOptions.SectionName)
        .Get<Clustral.Sdk.Crypto.CertificateAuthorityOptions>();

    var env = ctx.HostingEnvironment;
    if (caOpts is not null
        && !string.IsNullOrEmpty(caOpts.CaCertPath) && File.Exists(caOpts.CaCertPath)
        && !string.IsNullOrEmpty(caOpts.CaKeyPath) && File.Exists(caOpts.CaKeyPath)
        && !env.IsEnvironment("Testing"))
    {
        // Pre-load the CA cert + key for server TLS and client cert validation.
        var certPem = File.ReadAllText(caOpts.CaCertPath);
        var keyPem = File.ReadAllText(caOpts.CaKeyPath);
        var serverCert = System.Security.Cryptography.X509Certificates.X509Certificate2
            .CreateFromPem(certPem, keyPem);
        var caCertForValidation = new System.Security.Cryptography.X509Certificates.X509Certificate2(
            System.Security.Cryptography.X509Certificates.X509Certificate2
                .CreateFromPem(certPem));

        kestrel.ListenAnyIP(5443, listenOptions =>
        {
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificate = serverCert;
                // AllowCertificate (not Require) so bootstrap RPCs like RegisterAgent
                // can connect without a client cert. The AgentAuthInterceptor enforces
                // cert requirement at the RPC level for all non-bootstrap calls.
                httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.AllowCertificate;
                httpsOptions.ClientCertificateValidation = (cert, chain, errors) =>
                {
                    if (cert is null) return true; // No client cert — allowed for bootstrap RPCs

                    using var customChain = new System.Security.Cryptography.X509Certificates.X509Chain();
                    customChain.ChainPolicy.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
                    customChain.ChainPolicy.TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
                    customChain.ChainPolicy.CustomTrustStore.Add(caCertForValidation);
                    return customChain.Build(new System.Security.Cryptography.X509Certificates.X509Certificate2(cert));
                };
            });
        });
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// gRPC
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<AgentAuthInterceptor>();
builder.Services.AddGrpc(opts =>
{
    opts.Interceptors.Add<AgentAuthInterceptor>();
    opts.EnableDetailedErrors = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
});

// Singleton session registry shared between TunnelServiceImpl instances.
builder.Services.AddSingleton<TunnelSessionManager>();

// Background cleanup: expire pending access requests past their TTL.
builder.Services.AddHostedService<AccessRequestCleanupService>();

// ─────────────────────────────────────────────────────────────────────────────
// Health checks
// ─────────────────────────────────────────────────────────────────────────────

var oidcDiscoveryUrl = !string.IsNullOrEmpty(oidcOpts.MetadataAddress)
    ? oidcOpts.MetadataAddress
    : $"{oidcOpts.Authority.TrimEnd('/')}/.well-known/openid-configuration";

builder.Services.AddHealthChecks()
    .AddMongoDb(tags: ["ready"], name: "mongodb", timeout: TimeSpan.FromSeconds(5))
    .AddUrlGroup(new Uri(oidcDiscoveryUrl), "oidc", tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));

// ─────────────────────────────────────────────────────────────────────────────
// Rate limiting (proxy traffic, per-credential token bucket)
// ─────────────────────────────────────────────────────────────────────────────

var proxyOpts = builder.Configuration.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>() ?? new ProxyOptions();
if (proxyOpts.RateLimiting.Enabled)
{
    var rl = proxyOpts.RateLimiting;
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;
        options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
            context =>
            {
                var path = context.Request.Path.Value ?? "";
                if (!path.StartsWith("/proxy/", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith("/api/proxy/", StringComparison.OrdinalIgnoreCase))
                {
                    return System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("non-proxy");
                }

                var auth = context.Request.Headers.Authorization.FirstOrDefault();
                var key = auth is not null ? auth.GetHashCode().ToString() : "anonymous";
                return System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(key, _ =>
                    new System.Threading.RateLimiting.TokenBucketRateLimiterOptions
                    {
                        TokenLimit = rl.BurstSize,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        TokensPerPeriod = rl.RequestsPerSecond,
                        QueueLimit = rl.QueueSize,
                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                    });
            });
    });
}

// MediatR + FluentValidation vertical slicing infrastructure.
// ── MassTransit (publish integration events to RabbitMQ) ──────────────────
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration);

// ── OpenTelemetry (metrics + distributed tracing → Grafana) ──────────────
builder.Services.AddApplicationOpenTelemetry(builder.Configuration);

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(Clustral.Sdk.Cqs.ValidationBehavior<,>));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserProvider, HttpCurrentUserProvider>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Services.UserSyncService>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Services.ProxyAuthService>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Services.ImpersonationResolver>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Specifications.AccessSpecifications>();

// Repository interfaces — thin wrappers over ClustralDb collections.
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Repositories.IClusterRepository,
    Clustral.ControlPlane.Infrastructure.Repositories.MongoClusterRepository>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Repositories.IRoleRepository,
    Clustral.ControlPlane.Infrastructure.Repositories.MongoRoleRepository>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Repositories.IUserRepository,
    Clustral.ControlPlane.Infrastructure.Repositories.MongoUserRepository>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Repositories.IAccessRequestRepository,
    Clustral.ControlPlane.Infrastructure.Repositories.MongoAccessRequestRepository>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Repositories.IRoleAssignmentRepository,
    Clustral.ControlPlane.Infrastructure.Repositories.MongoRoleAssignmentRepository>();
builder.Services.AddScoped<Clustral.ControlPlane.Domain.Repositories.IAccessTokenRepository,
    Clustral.ControlPlane.Infrastructure.Repositories.MongoAccessTokenRepository>();
builder.Services.AddSingleton<TokenHashingService>();
builder.Services.AddScoped<Clustral.ControlPlane.Features.AccessRequests.AccessRequestEnricher>();

// ─────────────────────────────────────────────────────────────────────────────
// Build
// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─────────────────────────────────────────────────────────────────────────────
// Middleware pipeline
// ─────────────────────────────────────────────────────────────────────────────

// Global exception handler — must be first to catch all unhandled exceptions.
app.UseMiddleware<Clustral.ControlPlane.Api.GlobalExceptionHandlerMiddleware>();

app.UseHttpMetrics();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(opts =>
    {
        opts.SwaggerEndpoint("/swagger/v1/swagger.json", "Clustral Control Plane v1");
        opts.RoutePrefix = "swagger";
    });
}

// Rate limiting — before proxy so 429 responses are fast.
if (proxyOpts.RateLimiting.Enabled)
    app.UseRateLimiter();

// kubectl proxy — must be before UseRouting so it catches /proxy/* before MVC.
app.UseMiddleware<Clustral.ControlPlane.Api.KubectlProxyMiddleware>();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// REST endpoints
app.MapControllers();
app.MapMetrics(); // GET /metrics for Prometheus scraping

// gRPC endpoints
app.MapGrpcService<ClusterServiceImpl>();
app.MapGrpcService<TunnelServiceImpl>();
app.MapGrpcService<AuthServiceImpl>();

// ─────────────────────────────────────────────────────────────────────────────
// Health check endpoints
// ─────────────────────────────────────────────────────────────────────────────

// Liveness — is the process alive? No dependency checks.
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false, // run no checks — if the process responds, it's alive
}).AllowAnonymous();

// Readiness — can it serve traffic? Checks MongoDB + OIDC.
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

// Detailed — all checks with timings. Requires authentication.
app.MapHealthChecks("/healthz/detail", new HealthCheckOptions
{
    ResponseWriter = WriteDetailedHealthResponse,
}).RequireAuthorization();

// Public config endpoint — CLI auto-discovers OIDC settings from here
// so users only need to know the ControlPlane URL, not the OIDC provider URL.
app.MapGet("/api/v1/config", (IOptions<OidcOptions> oidc) =>
{
    var o = oidc.Value;
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";
    return Results.Ok(new
    {
        version,
        oidcAuthority = o.Authority,
        oidcClientId = "clustral-cli",
        oidcScopes = "openid email profile",
    });
}).AllowAnonymous();

// ─────────────────────────────────────────────────────────────────────────────
// Ensure MongoDB indexes on startup (after config validation).
// ─────────────────────────────────────────────────────────────────────────────

app.Lifetime.ApplicationStarted.Register(() =>
{
    var db = app.Services.GetRequiredService<ClustralDb>();
    db.EnsureIndexesAsync().GetAwaiter().GetResult();
});

app.Run();

// Marker class for WebApplicationFactory<Program> in integration tests.
public partial class Program
{
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;

    private static Task WriteDetailedHealthResponse(HttpContext context, HealthReport report)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var result = new
        {
            status = report.Status.ToString(),
            version,
            uptime = (DateTimeOffset.UtcNow - StartTime).ToString(@"dd\.hh\:mm\:ss"),
            totalDuration = report.TotalDuration.TotalMilliseconds.ToString("F0") + "ms",
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    duration = e.Value.Duration.TotalMilliseconds.ToString("F0") + "ms",
                    description = e.Value.Description,
                }),
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = report.Status == HealthStatus.Healthy ? 200 : 503;

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}