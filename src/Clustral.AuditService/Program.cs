using Clustral.AuditService.Infrastructure;
using Clustral.Sdk.Auth;
using Clustral.Sdk.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Serilog;

// ── MongoDB Guid serialization ───────────────────────────────────────────
// MongoDB.Driver 3.x requires explicit GuidRepresentation. Register a
// global serializer so Guid properties on integration events (used in
// ToBsonDocument()) serialize as Standard UUID strings.
try { BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard)); }
catch (BsonSerializationException) { /* already registered */ }

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ── MongoDB ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var connectionString = builder.Configuration["MongoDB:ConnectionString"]
                           ?? "mongodb://localhost:27017";
    return new MongoClient(connectionString);
});
builder.Services.AddSingleton<AuditDbContext>();
builder.Services.AddScoped<Clustral.AuditService.Domain.Repositories.IAuditEventRepository,
    MongoAuditEventRepository>();

// ── MassTransit (consume integration events from RabbitMQ) ───────────────
builder.Services.AddMassTransitWithRabbitMq(builder.Configuration,
    consumersAssembly: typeof(Program).Assembly);

// ── CQS + MediatR ───────────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ── Authentication — Internal JWT (ES256, issued by API Gateway) ─────
var internalJwtPublicKeyPath = builder.Configuration["InternalJwt:PublicKeyPath"];
if (!string.IsNullOrEmpty(internalJwtPublicKeyPath) && File.Exists(internalJwtPublicKeyPath))
{
    var publicKeyPem = File.ReadAllText(internalJwtPublicKeyPath);
    var internalJwt = InternalJwtService.ForValidation(publicKeyPem);

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            opts.TokenValidationParameters = internalJwt.GetValidationParameters();
            opts.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var internalToken = context.HttpContext.Request.Headers["X-Internal-Token"]
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(internalToken))
                        context.Token = internalToken;
                    return Task.CompletedTask;
                },
            };
        });
}
builder.Services.AddAuthorization();

// ── ASP.NET Core ─────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── Ensure MongoDB indexes ───────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    await db.EnsureIndexesAsync();
}

// ── Middleware ────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Make Program class accessible to WebApplicationFactory in tests.
public partial class Program;
