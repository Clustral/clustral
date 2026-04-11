using Prometheus;
using Clustral.AuditService.Infrastructure;
using Clustral.Sdk.Messaging;
using Clustral.Sdk.Telemetry;
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
builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    var otlpEndpoint = ctx.Configuration["OpenTelemetry:LogsEndpoint"];
    var serviceName = ctx.Configuration["OpenTelemetry:ServiceName"];
    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        lc.WriteTo.OpenTelemetry(sinkOptions =>
        {
            sinkOptions.Endpoint = otlpEndpoint;
            sinkOptions.ResourceAttributes.Add("service.name", serviceName ?? "clustral-audit-service");
        });
    }
});

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

// ── OpenTelemetry (metrics + distributed tracing → Grafana) ──────────────
builder.Services.AddApplicationOpenTelemetry(builder.Configuration);

// ── CQS + MediatR ───────────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

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

app.UseHttpMetrics();
app.UseSerilogRequestLogging();
app.MapControllers();
app.MapMetrics(); // GET /metrics for Prometheus scraping

app.Run();

// Make Program class accessible to WebApplicationFactory in tests.
public partial class Program;
