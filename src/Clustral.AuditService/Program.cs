using Clustral.AuditService.Infrastructure;
using Clustral.Sdk.Messaging;
using Clustral.Sdk.Telemetry;
using MongoDB.Driver;
using Serilog;

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

app.UseSerilogRequestLogging();
app.MapControllers();

app.Run();

// Make Program class accessible to WebApplicationFactory in tests.
public partial class Program;
