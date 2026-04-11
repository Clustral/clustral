using System.Net;
using System.Net.Http.Json;
using Clustral.AuditService.Api.Controllers;
using Clustral.AuditService.Domain;
using Clustral.AuditService.Infrastructure;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Clustral.AuditService.Tests.Api;

[Collection("Mongo")]
public sealed class AuditControllerTests(MongoFixture mongo, ITestOutputHelper output)
    : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["MongoDB:ConnectionString"] = mongo.ConnectionString,
                        ["MongoDB:DatabaseName"] = $"test-api-{Guid.NewGuid():N}",
                        // Disable RabbitMQ for API-only tests.
                        ["RabbitMQ:Host"] = "localhost",
                        ["RabbitMQ:Port"] = "5672",
                        ["RabbitMQ:VHost"] = "/",
                        ["RabbitMQ:User"] = "guest",
                        ["RabbitMQ:Pass"] = "guest",
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Replace MongoDB client to point at Testcontainers.
                    services.RemoveAll<IMongoClient>();
                    services.AddSingleton<IMongoClient>(_ => new MongoClient(mongo.ConnectionString));

                    // Replace MassTransit RabbitMQ transport with in-memory to avoid
                    // requiring a running RabbitMQ instance for API-only tests.
                    services.RemoveAll<IBusControl>();
                    services.AddMassTransit(cfg => cfg.UsingInMemory());
                });
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task List_EmptyDatabase_ReturnsEmptyPage()
    {
        var response = await _client.GetAsync("/api/v1/audit");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        output.WriteLine($"TotalCount: {result?.TotalCount}");

        result.Should().NotBeNull();
        result!.Events.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task List_WithSeededEvents_ReturnsPaginated()
    {
        // Seed events directly into MongoDB.
        var dbContext = GetDbContext();
        var events = Enumerable.Range(1, 5).Select(i => new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = "access_request.created",
            Code = EventCodes.AccessRequestCreated,
            Category = "access_requests",
            Severity = Severity.Info,
            Success = true,
            User = $"user{i}@example.com",
            Time = DateTimeOffset.UtcNow.AddMinutes(-i),
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Test event {i}",
        }).ToList();

        await dbContext.AuditEvents.InsertManyAsync(events);

        var response = await _client.GetAsync("/api/v1/audit?pageSize=2&page=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        output.WriteLine($"TotalCount: {result?.TotalCount}, Events: {result?.Events.Count}");

        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(3);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task List_FilterByCategory_ReturnsOnlyMatchingEvents()
    {
        var dbContext = GetDbContext();

        await dbContext.AuditEvents.InsertManyAsync(
        [
            CreateEvent("access_requests", EventCodes.AccessRequestCreated, "access_request.created"),
            CreateEvent("credentials", EventCodes.CredentialIssued, "credential.issued"),
            CreateEvent("clusters", EventCodes.ClusterRegistered, "cluster.registered"),
        ]);

        var response = await _client.GetAsync("/api/v1/audit?category=credentials");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        output.WriteLine($"Filtered count: {result?.TotalCount}");

        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(1);
        result.Events[0].Category.Should().Be("credentials");
        result.Events[0].Code.Should().Be(EventCodes.CredentialIssued);
    }

    [Fact]
    public async Task List_FilterBySeverity_ReturnsOnlyMatchingSeverity()
    {
        var dbContext = GetDbContext();

        await dbContext.AuditEvents.InsertManyAsync(
        [
            CreateEvent("access_requests", EventCodes.AccessRequestCreated, "access_request.created", Severity.Info),
            CreateEvent("access_requests", EventCodes.AccessRequestDenied, "access_request.denied", Severity.Warning),
        ]);

        var response = await _client.GetAsync("/api/v1/audit?severity=Warning");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(1);
        result.Events[0].Severity.Should().Be("Warning");
    }

    [Fact]
    public async Task List_FilterByUser_ReturnsOnlyMatchingUser()
    {
        var dbContext = GetDbContext();

        await dbContext.AuditEvents.InsertManyAsync(
        [
            CreateEvent("access_requests", EventCodes.AccessRequestCreated, "access_request.created", user: "alice@example.com"),
            CreateEvent("access_requests", EventCodes.AccessRequestApproved, "access_request.approved", user: "bob@example.com"),
        ]);

        var response = await _client.GetAsync("/api/v1/audit?user=alice@example.com");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(1);
        result.Events[0].User.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task List_FilterByClusterId_ReturnsOnlyMatchingCluster()
    {
        var dbContext = GetDbContext();
        var targetClusterId = Guid.NewGuid();
        var otherClusterId = Guid.NewGuid();

        await dbContext.AuditEvents.InsertManyAsync(
        [
            CreateEvent("clusters", EventCodes.ClusterRegistered, "cluster.registered", clusterId: targetClusterId),
            CreateEvent("clusters", EventCodes.ClusterRegistered, "cluster.registered", clusterId: otherClusterId),
        ]);

        var response = await _client.GetAsync($"/api/v1/audit?clusterId={targetClusterId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(1);
        result.Events[0].ClusterId.Should().Be(targetClusterId);
    }

    [Fact]
    public async Task List_FilterByTimeRange_ReturnsOnlyEventsInRange()
    {
        var dbContext = GetDbContext();
        var now = DateTimeOffset.UtcNow;

        await dbContext.AuditEvents.InsertManyAsync(
        [
            CreateEvent("access_requests", EventCodes.AccessRequestCreated, "access_request.created", time: now.AddHours(-3)),
            CreateEvent("access_requests", EventCodes.AccessRequestApproved, "access_request.approved", time: now.AddHours(-1)),
            CreateEvent("access_requests", EventCodes.AccessRequestExpired, "access_request.expired", time: now.AddMinutes(-10)),
        ]);

        var from = now.AddHours(-2).ToString("O");
        var to = now.AddMinutes(-30).ToString("O");
        var response = await _client.GetAsync($"/api/v1/audit?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();
        output.WriteLine($"Time-filtered count: {result?.TotalCount}");
        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(1);
        result.Events[0].Code.Should().Be(EventCodes.AccessRequestApproved);
    }

    [Fact]
    public async Task List_ResultsOrderedByTimeDescending()
    {
        var dbContext = GetDbContext();
        var now = DateTimeOffset.UtcNow;

        await dbContext.AuditEvents.InsertManyAsync(
        [
            CreateEvent("access_requests", EventCodes.AccessRequestCreated, "access_request.created", time: now.AddMinutes(-30)),
            CreateEvent("access_requests", EventCodes.AccessRequestApproved, "access_request.approved", time: now.AddMinutes(-10)),
            CreateEvent("access_requests", EventCodes.AccessRequestExpired, "access_request.expired", time: now.AddMinutes(-20)),
        ]);

        var response = await _client.GetAsync("/api/v1/audit");
        var result = await response.Content.ReadFromJsonAsync<AuditListResponse>();

        result.Should().NotBeNull();
        result!.Events.Should().HaveCount(3);
        result.Events[0].Code.Should().Be(EventCodes.AccessRequestApproved);   // newest
        result.Events[1].Code.Should().Be(EventCodes.AccessRequestExpired);
        result.Events[2].Code.Should().Be(EventCodes.AccessRequestCreated);    // oldest
    }

    [Fact]
    public async Task List_InvalidPage_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/v1/audit?page=0");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_InvalidPageSize_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/v1/audit?pageSize=201");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_ExistingEvent_ReturnsEvent()
    {
        var dbContext = GetDbContext();
        var uid = Guid.NewGuid();

        await dbContext.AuditEvents.InsertOneAsync(new AuditEvent
        {
            Uid = uid,
            Event = "credential.issued",
            Code = EventCodes.CredentialIssued,
            Category = "credentials",
            Severity = Severity.Info,
            Success = true,
            User = "alice@example.com",
            Time = DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = "Test credential event",
        });

        var response = await _client.GetAsync($"/api/v1/audit/{uid}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AuditEventResponse>();
        output.WriteLine($"Got event: {result?.Code}");

        result.Should().NotBeNull();
        result!.Uid.Should().Be(uid);
        result.Code.Should().Be(EventCodes.CredentialIssued);
        result.Category.Should().Be("credentials");
        result.Severity.Should().Be("Info");
        result.User.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/v1/audit/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the <see cref="AuditDbContext"/> from the test server's DI container
    /// so we can seed data directly into MongoDB.
    /// </summary>
    private AuditDbContext GetDbContext()
        => _factory.Services.GetRequiredService<AuditDbContext>();

    private static AuditEvent CreateEvent(
        string category,
        string code,
        string eventName,
        Severity severity = Severity.Info,
        string? user = null,
        Guid? clusterId = null,
        DateTimeOffset? time = null)
    {
        return new AuditEvent
        {
            Uid = Guid.NewGuid(),
            Event = eventName,
            Code = code,
            Category = category,
            Severity = severity,
            Success = severity != Severity.Warning,
            User = user,
            ClusterId = clusterId,
            Time = time ?? DateTimeOffset.UtcNow,
            ReceivedAt = DateTimeOffset.UtcNow,
            Message = $"Test {eventName}",
        };
    }
}
