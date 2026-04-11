using System.Threading.RateLimiting;
using Clustral.Sdk.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Serilog;
using Serilog.Context;

// ── Bootstrap logger ─────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ── YARP Reverse Proxy ───────────────────────────────────────────────────────
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ── Authentication (OIDC JWT from any provider) ──────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var metadataAddress = builder.Configuration["Oidc:MetadataAddress"];
        if (!string.IsNullOrEmpty(metadataAddress))
        {
            // Use internal Docker hostname for JWKS fetch, skip issuer validation
            // since the token issuer varies by access URL (LAN IP vs localhost).
            options.Authority = null;
            options.MetadataAddress = metadataAddress;
        }
        else
        {
            options.Authority = builder.Configuration["Oidc:Authority"];
        }

        options.Audience = builder.Configuration["Oidc:Audience"];
        options.RequireHttpsMetadata =
            bool.TryParse(builder.Configuration["Oidc:RequireHttpsMetadata"], out var v) && v;

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = false,    // issuer varies by access URL
            ValidateAudience = false,  // tokens come from multiple clients (CLI, Web UI)
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true, // JWKS key check proves token authenticity
            NameClaimType = "preferred_username",
        };
    });
builder.Services.AddAuthorization();

// ── Internal JWT service (ES256 — signs tokens for downstream services) ──────
var privateKeyPath = builder.Configuration["InternalJwt:PrivateKeyPath"];
if (!string.IsNullOrEmpty(privateKeyPath) && File.Exists(privateKeyPath))
{
    var privateKeyPem = File.ReadAllText(privateKeyPath);
    builder.Services.AddSingleton(InternalJwtService.ForSigning(privateKeyPem));
}

// ── CORS ─────────────────────────────────────────────────────────────────────
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["https://localhost"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ── Rate Limiting ────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100,
                TokensPerPeriod = 50,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                AutoReplenishment = true,
            }));
});

// ── Request Size Limits ──────────────────────────────────────────────────────
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
});

var app = builder.Build();

// ── Middleware Pipeline ──────────────────────────────────────────────────────

app.UseCors();
app.UseSerilogRequestLogging();

// Correlation ID — generate or preserve from incoming request
app.Use(async (ctx, next) =>
{
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");
    ctx.Request.Headers["X-Correlation-Id"] = correlationId;
    ctx.Response.Headers["X-Correlation-Id"] = correlationId;
    using (LogContext.PushProperty("CorrelationId", correlationId))
        await next();
});

app.UseAuthentication();
app.UseAuthorization();

// Internal JWT — after OIDC validation, issue ES256 token for downstream
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var jwtService = ctx.RequestServices.GetService<InternalJwtService>();
        if (jwtService is not null)
        {
            var internalToken = jwtService.Issue(ctx.User.Claims);
            ctx.Request.Headers["X-Internal-Token"] = internalToken;
        }
    }
    await next();
});

app.UseRateLimiter();
app.MapReverseProxy();

app.Run();

// Make Program class accessible to WebApplicationFactory in tests.
public partial class Program;
