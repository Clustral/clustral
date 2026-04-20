using System.Reflection;
using System.Text.Json;
using Clustral.ControlPlane.Features.Proxy;
using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Infrastructure.Auth;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Messaging;
using Clustral.ControlPlane.Infrastructure.Redis;
using Clustral.ControlPlane.Infrastructure.Tunnel;
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

// Error documentation base URL — every RFC 7807 `type` field and plain-text
// `Link: rel="help"` header points here. Air-gapped deployments override
// with `Errors:DocsBaseUrl` in appsettings / env. Null/empty keeps the
// default (https://docs.clustral.kube.it.com/errors/).
Clustral.Sdk.Http.ErrorDocumentation.SetBaseUrl(builder.Configuration["Errors:DocsBaseUrl"]);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ─────────────────────────────────────────────────────────────────────────────
// Options — validated at startup via FluentValidation + ValidateOnStart().
// If any required config is missing, the app aborts immediately with a clear error.
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddOptionsWithValidation<CredentialOptions, CredentialOptionsValidator>(CredentialOptions.SectionName);

builder.Services.AddOptionsWithValidation<MongoDbOptions, MongoDbOptionsValidator>(MongoDbOptions.SectionName);

builder.Services.AddOptionsWithValidation<ProxyOptions, ProxyOptionsValidator>(ProxyOptions.SectionName);


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
// Authentication — Internal JWT (ES256, issued by API Gateway)
//
// The API Gateway validates external OIDC JWTs and issues short-lived
// internal JWTs (ES256) forwarded via X-Internal-Token header.
// The ControlPlane validates using only the public key.
// ─────────────────────────────────────────────────────────────────────────────

var internalJwtPublicKeyPath = builder.Configuration["InternalJwt:PublicKeyPath"];
if (!string.IsNullOrEmpty(internalJwtPublicKeyPath) && File.Exists(internalJwtPublicKeyPath))
{
    var publicKeyPem = File.ReadAllText(internalJwtPublicKeyPath);
    var internalJwt = InternalJwtService.ForValidation(publicKeyPem);
    builder.Services.AddSingleton(internalJwt);

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.TokenValidationParameters = internalJwt.GetValidationParameters();

            // Read token from X-Internal-Token header instead of Authorization
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var internalToken = context.HttpContext.Request.Headers["X-Internal-Token"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(internalToken))
                        context.Token = internalToken;

                    return Task.CompletedTask;
                },
            };
        });
}

builder.Services.AddAuthorization();

// ── Kubeconfig JWT service (ES256 — signs kubeconfig credential JWTs) ────────
var kubeconfigJwtPrivateKeyPath = builder.Configuration["KubeconfigJwt:PrivateKeyPath"];
if (!string.IsNullOrEmpty(kubeconfigJwtPrivateKeyPath) && File.Exists(kubeconfigJwtPrivateKeyPath))
{
    var privateKeyPem = File.ReadAllText(kubeconfigJwtPrivateKeyPath);
    builder.Services.AddSingleton(KubeconfigJwtService.ForSigning(privateKeyPem));
}

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

builder.Services.AddMemoryCache();

// ─────────────────────────────────────────────────────────────────────────────
// Redis session registry + TunnelProxy gRPC client
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddSingleton<IRedisSessionRegistry, RedisSessionRegistry>();
builder.Services.AddSingleton<ITunnelProxyClient, TunnelProxyClient>();

// Background cleanup: expire pending access requests past their TTL.
builder.Services.AddHostedService<AccessRequestCleanupService>();

// ─────────────────────────────────────────────────────────────────────────────
// Health checks
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddHealthChecks()
    .AddMongoDb(tags: ["ready"], name: "mongodb", timeout: TimeSpan.FromSeconds(5));

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

// Correlation ID — first so every downstream log line and error body carries it.
app.UseMiddleware<Clustral.Sdk.Http.CorrelationIdMiddleware>();

// Global exception handler — catches unhandled exceptions and writes RFC 7807.
app.UseMiddleware<Clustral.Sdk.Http.GlobalExceptionHandlerMiddleware>();

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

// Public version endpoint — CLI uses this for version checks and health probes.
// OIDC discovery is served by the Web UI via /.well-known/clustral-configuration.
app.MapGet("/api/v1/version", () =>
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";
    return Results.Ok(new { version });
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