using System.Threading.RateLimiting;
using Clustral.ApiGateway.Api;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Yarp.ReverseProxy.Transforms;
using Serilog;

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
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        // Add internal JWT as a request transform so YARP forwards it.
        ctx.AddRequestTransform(transformCtx =>
        {
            if (transformCtx.HttpContext.User.Identity?.IsAuthenticated == true)
            {
                var jwtService = transformCtx.HttpContext.RequestServices
                    .GetService<InternalJwtService>();
                if (jwtService is not null)
                {
                    var internalToken = jwtService.Issue(
                        transformCtx.HttpContext.User.Claims);
                    transformCtx.ProxyRequest.Headers.Remove("X-Internal-Token");
                    transformCtx.ProxyRequest.Headers.Add(
                        "X-Internal-Token", internalToken);
                }
            }
            return ValueTask.CompletedTask;
        });
    });

// ── Authentication ───────────────────────────────────────────────────────────
//
// Two distinct JWT types arrive at the gateway, each with its own validation:
//
//   1. OIDC JWT — issued by the external OIDC provider (Keycloak/Auth0/etc.)
//      Validated with JWKS. Expected issuer = Oidc:Authority (or Oidc:ValidIssuers),
//      expected audience = Oidc:Audience.
//
//   2. Kubeconfig JWT — issued by the ControlPlane (ES256-signed) for kubectl.
//      Expected issuer = "clustral-controlplane", audience = "clustral-kubeconfig".
//
// A policy scheme inspects the incoming token's "kind" claim and routes to the
// correct scheme. Each scheme enforces strict issuer + audience validation —
// a compromised OIDC key cannot forge a kubeconfig JWT and vice versa.
// ─────────────────────────────────────────────────────────────────────────────

const string OidcScheme = "OidcJwt";
const string KubeconfigScheme = "KubeconfigJwt";

var oidcAuthority = builder.Configuration["Oidc:Authority"];
var oidcMetadata = builder.Configuration["Oidc:MetadataAddress"];
var oidcAudience = builder.Configuration["Oidc:Audience"];
var oidcRequireHttps =
    bool.TryParse(builder.Configuration["Oidc:RequireHttpsMetadata"], out var reqHttps) && reqHttps;
var nameClaimType = builder.Configuration["Oidc:NameClaimType"] ?? "preferred_username";

// Optional: accept additional issuer values (e.g., when the same OIDC provider
// is reached via multiple URLs in dev — LAN IP vs localhost). In production,
// configure the OIDC provider with a canonical hostname and leave this empty.
var extraValidIssuers = builder.Configuration.GetSection("Oidc:ValidIssuers").Get<string[]>()
                        ?? [];
var oidcValidIssuers = new List<string>(extraValidIssuers);
if (!string.IsNullOrEmpty(oidcAuthority) && !oidcValidIssuers.Contains(oidcAuthority))
    oidcValidIssuers.Add(oidcAuthority);

// Audiences: accept the primary Oidc:Audience plus any additional values from
// Oidc:ValidAudiences (e.g., when the Web UI and CLI use different OIDC clients
// and tokens carry different audience claims).
var extraValidAudiences = builder.Configuration.GetSection("Oidc:ValidAudiences").Get<string[]>()
                          ?? [];
var oidcValidAudiences = new List<string>(extraValidAudiences);
if (!string.IsNullOrEmpty(oidcAudience) && !oidcValidAudiences.Contains(oidcAudience))
    oidcValidAudiences.Add(oidcAudience);

// Load kubeconfig JWT public key (for validating ControlPlane-signed tokens).
var kubeconfigPublicKeyPath = builder.Configuration["KubeconfigJwt:PublicKeyPath"];
Clustral.Sdk.Auth.KubeconfigJwtService? kubeconfigJwt = null;
if (!string.IsNullOrEmpty(kubeconfigPublicKeyPath) && File.Exists(kubeconfigPublicKeyPath))
{
    var publicKeyPem = File.ReadAllText(kubeconfigPublicKeyPath);
    kubeconfigJwt = Clustral.Sdk.Auth.KubeconfigJwtService.ForValidation(publicKeyPem);
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddPolicyScheme(JwtBearerDefaults.AuthenticationScheme, "JWT-Router", options =>
    {
        // Inspect the incoming token's "kind" claim to decide which scheme validates it.
        // Unrecognized tokens default to the OIDC scheme (which will reject them if invalid).
        options.ForwardDefaultSelector = ctx =>
        {
            var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) &&
                authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    var kind = jwt.Claims
                        .FirstOrDefault(c => c.Type == Clustral.Sdk.Auth.KubeconfigJwtService.KindClaim)
                        ?.Value;
                    if (kind == Clustral.Sdk.Auth.KubeconfigJwtService.KindValue)
                        return KubeconfigScheme;
                }
            }
            return OidcScheme;
        };
    })
    .AddJwtBearer(OidcScheme, options =>
    {
        if (!string.IsNullOrEmpty(oidcMetadata))
        {
            // Use configured metadata URL (e.g., Docker-internal hostname for JWKS).
            options.Authority = null;
            options.MetadataAddress = oidcMetadata;
        }
        else
        {
            options.Authority = oidcAuthority;
        }

        options.Audience = oidcAudience;
        options.RequireHttpsMetadata = oidcRequireHttps;

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = oidcValidIssuers.Count > 0,
            ValidIssuers = oidcValidIssuers,
            ValidateAudience = oidcValidAudiences.Count > 0,
            ValidAudiences = oidcValidAudiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = nameClaimType,
        };
        options.Events = GatewayJwtEvents.Create();
    })
    .AddJwtBearer(KubeconfigScheme, options =>
    {
        if (kubeconfigJwt is not null)
        {
            // Es256JwtService.GetValidationParameters() already enforces
            // issuer=clustral-controlplane, audience=clustral-kubeconfig,
            // alg=ES256, and signing key = configured public key.
            options.TokenValidationParameters = kubeconfigJwt.GetValidationParameters();
            options.TokenValidationParameters.NameClaimType = nameClaimType;
        }
        else
        {
            // No kubeconfig public key configured — reject all kubeconfig tokens.
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Clustral.Sdk.Auth.KubeconfigJwtService.IssuerName,
                ValidateAudience = true,
                ValidAudience = Clustral.Sdk.Auth.KubeconfigJwtService.AudienceName,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
            };
        }
        options.Events = GatewayJwtEvents.Create();
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

    // Replace ASP.NET's default empty 429 body with a path-aware error body.
    options.OnRejected = async (ctx, _) =>
    {
        await GatewayErrorWriter.WriteAsync(ctx.HttpContext,
            statusCode: StatusCodes.Status429TooManyRequests,
            code: "RATE_LIMITED",
            message: "Request rate limit exceeded. Please retry later.");
    };
});

// ── Request Size Limits ──────────────────────────────────────────────────────
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
});

// ── Health Checks ───────────────────────────────────────────────────────────
var oidcDiscoveryUrl = !string.IsNullOrEmpty(oidcMetadata)
    ? oidcMetadata
    : !string.IsNullOrEmpty(oidcAuthority)
        ? $"{oidcAuthority.TrimEnd('/')}/.well-known/openid-configuration"
        : null;

var hcBuilder = builder.Services.AddHealthChecks();
if (!string.IsNullOrEmpty(oidcDiscoveryUrl))
{
    hcBuilder.AddUrlGroup(new Uri(oidcDiscoveryUrl), "oidc", tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));
}

var app = builder.Build();

// ── Middleware Pipeline ──────────────────────────────────────────────────────

// Correlation ID — first so every downstream log line and error body carries it.
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseCors();
app.UseSerilogRequestLogging();

// Terminal status-code handler — writes a well-described body for any
// 4xx/5xx response that reached the client with an empty body (e.g., YARP
// 502 destination-unreachable, 404 no-route, CORS preflight rejections).
app.UseStatusCodePages(async statusCtx =>
{
    var ctx = statusCtx.HttpContext;
    if (ctx.Response.ContentLength > 0 || ctx.Response.HasStarted) return;

    var (code, message) = ctx.Response.StatusCode switch
    {
        404 => ("ROUTE_NOT_FOUND",     "No route matches this request."),
        502 => ("UPSTREAM_UNREACHABLE", "Upstream service is unreachable."),
        503 => ("UPSTREAM_UNAVAILABLE", "Upstream service is unavailable."),
        504 => ("UPSTREAM_TIMEOUT",     "Upstream service did not respond in time."),
        _   => ("GATEWAY_ERROR",        $"Gateway returned status {ctx.Response.StatusCode}."),
    };
    await GatewayErrorWriter.WriteAsync(ctx, ctx.Response.StatusCode, code, message);
});

app.UseAuthentication();
app.UseAuthorization();

// Internal JWT is now issued via YARP request transform (not middleware)
// so it's correctly forwarded to downstream services.

app.UseRateLimiter();

// Gateway's own health check — verifies OIDC provider reachability.
// Distinct from /healthz which proxies to ControlPlane via YARP.
app.MapHealthChecks("/gateway/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false, // liveness — no checks
}).AllowAnonymous();
app.MapHealthChecks("/gateway/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

app.MapReverseProxy();

app.Run();

// Make Program class accessible to WebApplicationFactory in tests.
public partial class Program;
