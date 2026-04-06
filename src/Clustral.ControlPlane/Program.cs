using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Infrastructure.Auth;
using Clustral.ControlPlane.Protos;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
// gRPC
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddGrpc(opts => { opts.EnableDetailedErrors = builder.Environment.IsDevelopment(); });

// Singleton session registry shared between TunnelServiceImpl instances.
builder.Services.AddSingleton<TunnelSessionManager>();

// Background cleanup: expire pending access requests past their TTL.
builder.Services.AddHostedService<AccessRequestCleanupService>();

// MediatR + FluentValidation vertical slicing infrastructure.
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserProvider, HttpCurrentUserProvider>();
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

// kubectl proxy — must be before UseRouting so it catches /proxy/* before MVC.
app.UseMiddleware<Clustral.ControlPlane.Api.KubectlProxyMiddleware>();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// REST endpoints
app.MapControllers();

// gRPC endpoints
app.MapGrpcService<ClusterServiceImpl>();
app.MapGrpcService<TunnelServiceImpl>();
app.MapGrpcService<AuthServiceImpl>();

// Health check — used by docker-compose and Kubernetes readiness probes.
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();

// Public config endpoint — CLI auto-discovers OIDC settings from here
// so users only need to know the ControlPlane URL, not the OIDC provider URL.
app.MapGet("/api/v1/config", (IOptions<OidcOptions> oidc) =>
{
    var o = oidc.Value;
    return Results.Ok(new
    {
        oidcAuthority = o.Authority,
        oidcClientId = "clustral-cli",
        oidcScopes = "openid email profile",
    });
}).AllowAnonymous();

// ─────────────────────────────────────────────────────────────────────────────
// Ensure MongoDB indexes on startup.
// ─────────────────────────────────────────────────────────────────────────────

var db = app.Services.GetRequiredService<ClustralDb>();
await db.EnsureIndexesAsync();

app.Run();

// Marker class for WebApplicationFactory<Program> in integration tests.
public partial class Program
{
}