using Clustral.ControlPlane.Infrastructure;
using Clustral.ControlPlane.Infrastructure.Auth;
using Clustral.ControlPlane.Protos;
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
// Options
// ─────────────────────────────────────────────────────────────────────────────

builder.Services
    .AddOptions<KeycloakOptions>()
    .BindConfiguration(KeycloakOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

var keycloakSection = builder.Configuration.GetSection(KeycloakOptions.SectionName);
var keycloakOpts    = keycloakSection.Get<KeycloakOptions>()!;

// ─────────────────────────────────────────────────────────────────────────────
// MongoDB
// ─────────────────────────────────────────────────────────────────────────────

var mongoConnectionString = builder.Configuration.GetConnectionString("Clustral")
                            ?? "mongodb://localhost:27017";
var mongoDatabaseName     = builder.Configuration["MongoDB:DatabaseName"] ?? "clustral";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton(sp =>
    new ClustralDb(sp.GetRequiredService<IMongoClient>(), mongoDatabaseName));

// ─────────────────────────────────────────────────────────────────────────────
// Authentication — Keycloak OIDC (JWT Bearer)
// ─────────────────────────────────────────────────────────────────────────────

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.RequireHttpsMetadata = keycloakOpts.RequireHttpsMetadata;
        opts.Audience             = string.IsNullOrEmpty(keycloakOpts.Audience)
            ? keycloakOpts.ClientId
            : keycloakOpts.Audience;

        if (!string.IsNullOrEmpty(keycloakOpts.MetadataAddress))
        {
            // When MetadataAddress is set, the ControlPlane fetches JWKS from
            // an internal URL but the token issuer varies depending on how the
            // user accessed Keycloak (localhost vs LAN IP vs hostname).
            // Set Authority to null and use MetadataAddress only — this
            // prevents the JwtBearer middleware from doing its own issuer
            // validation against the Authority URL.
            opts.Authority       = null;
            opts.MetadataAddress = keycloakOpts.MetadataAddress;
        }
        else
        {
            opts.Authority = keycloakOpts.Authority;
        }

        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = false,   // issuer varies by access URL
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,    // JWKS key check proves authenticity
            ClockSkew                = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// ─────────────────────────────────────────────────────────────────────────────
// MVC Controllers + OpenAPI
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddScoped<Clustral.ControlPlane.Api.UserSyncFilter>();
builder.Services.AddControllers(opts =>
{
    opts.Filters.AddService<Clustral.ControlPlane.Api.UserSyncFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.SwaggerDoc("v1", new() { Title = "Clustral Control Plane", Version = "v1" });

    // Wire up the Keycloak bearer token in the Swagger UI.
    opts.AddSecurityDefinition("Bearer", new()
    {
        Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        Description  = "Paste a Keycloak access token (obtained via `clustral login`).",
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

builder.Services.AddGrpc(opts =>
{
    opts.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Singleton session registry shared between TunnelServiceImpl instances.
builder.Services.AddSingleton<TunnelSessionManager>();

// Background cleanup: expire pending access requests past their TTL.
builder.Services.AddHostedService<AccessRequestCleanupService>();

// ─────────────────────────────────────────────────────────────────────────────
// Build
// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ─────────────────────────────────────────────────────────────────────────────
// Middleware pipeline
// ─────────────────────────────────────────────────────────────────────────────

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
// so users only need to know the ControlPlane URL, not the Keycloak URL.
app.MapGet("/api/v1/config", (IOptions<KeycloakOptions> kc) =>
{
    var o = kc.Value;
    return Results.Ok(new
    {
        oidcAuthority = o.Authority,
        oidcClientId  = "clustral-cli",
        oidcScopes    = "openid email profile",
    });
}).AllowAnonymous();

// ─────────────────────────────────────────────────────────────────────────────
// Ensure MongoDB indexes on startup.
// ─────────────────────────────────────────────────────────────────────────────

var db = app.Services.GetRequiredService<ClustralDb>();
await db.EnsureIndexesAsync();

app.Run();
