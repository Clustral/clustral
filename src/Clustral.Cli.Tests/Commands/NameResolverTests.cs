using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Text;
using Clustral.Cli.Commands;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <see cref="NameResolver"/> — the shared name/GUID → ID resolver
/// used by <c>kube login</c> and <c>access request</c>.
///
/// Each test uses a <see cref="FakeHandler"/> that stubs the HTTP response
/// from <c>GET /api/v1/clusters</c> or <c>GET /api/v1/roles</c>. No real
/// ControlPlane is needed.
/// </summary>
public sealed class NameResolverTests(ITestOutputHelper output)
{
    // ─────────────────────────────────────────────────────────────────────────
    // Cluster resolution
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveCluster_GuidInput_ReturnsImmediately_NoHttpCall()
    {
        var guid = Guid.NewGuid().ToString();
        // Handler that throws if called — proves no HTTP request is made.
        var handler = new FakeHandler(_ => throw new InvalidOperationException("Should not be called"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, guid, ctx, CancellationToken.None);

        output.WriteLine($"Input: {guid} => Result: {result}");
        result.Should().Be(guid);
        exitCode().Should().Be(0, "GUID fast-path should not set an error exit code");
    }

    [Fact]
    public async Task ResolveCluster_NameMatch_CaseInsensitive()
    {
        var handler = new FakeHandler(_ => ClustersJson(
            ("11111111-1111-1111-1111-111111111111", "prod"),
            ("22222222-2222-2222-2222-222222222222", "staging")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, "PROD", ctx, CancellationToken.None);

        output.WriteLine($"Input: PROD => Result: {result}");
        result.Should().Be("11111111-1111-1111-1111-111111111111");
        exitCode().Should().Be(0);
    }

    [Fact]
    public async Task ResolveCluster_ExactMatchOnly_NoSubstringMatch()
    {
        var handler = new FakeHandler(_ => ClustersJson(
            ("11111111-1111-1111-1111-111111111111", "prod-east"),
            ("22222222-2222-2222-2222-222222222222", "prod-west")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, "prod", ctx, CancellationToken.None);

        output.WriteLine($"Input: prod => Result: {result}");
        result.Should().BeNull("'prod' does not exactly match 'prod-east' or 'prod-west'");
        exitCode().Should().Be(1);
    }

    [Fact]
    public async Task ResolveCluster_NotFound_WritesErrorAndReturnsNull()
    {
        var handler = new FakeHandler(_ => ClustersJson(
            ("11111111-1111-1111-1111-111111111111", "prod")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, "nonexistent", ctx, CancellationToken.None);

        output.WriteLine($"Input: nonexistent => Result: {result}");
        result.Should().BeNull();
        exitCode().Should().Be(1);
    }

    [Fact]
    public async Task ResolveCluster_AmbiguousMatch_WritesErrorAndReturnsNull()
    {
        // Two clusters with the same name (different IDs).
        var handler = new FakeHandler(_ => ClustersJson(
            ("11111111-1111-1111-1111-111111111111", "prod"),
            ("22222222-2222-2222-2222-222222222222", "prod")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, "prod", ctx, CancellationToken.None);

        output.WriteLine($"Input: prod (ambiguous) => Result: {result}");
        result.Should().BeNull();
        exitCode().Should().Be(1);
    }

    [Fact]
    public async Task ResolveCluster_EmptyList_ReturnsNull()
    {
        var handler = new FakeHandler(_ => """{"clusters":[]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, "anything", ctx, CancellationToken.None);

        output.WriteLine($"Input: anything (empty list) => Result: {result}");
        result.Should().BeNull();
        exitCode().Should().Be(1);
    }

    [Fact]
    public async Task ResolveCluster_HttpTimeout_WritesErrorAndReturnsNull()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("simulated timeout"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, "prod", ctx, CancellationToken.None);

        output.WriteLine($"Input: prod (timeout) => Result: {result}");
        result.Should().BeNull();
        exitCode().Should().Be(1);
    }

    [Fact]
    public async Task ResolveCluster_ServerError_WritesErrorAndReturnsNull()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("500 Internal Server Error"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveClusterIdAsync(http, "prod", ctx, CancellationToken.None);

        output.WriteLine($"Input: prod (server error) => Result: {result}");
        result.Should().BeNull();
        exitCode().Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Role resolution
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveRole_GuidInput_ReturnsImmediately_NoHttpCall()
    {
        var guid = Guid.NewGuid().ToString();
        var handler = new FakeHandler(_ => throw new InvalidOperationException("Should not be called"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveRoleIdAsync(http, guid, ctx, CancellationToken.None);

        output.WriteLine($"Input: {guid} => Result: {result}");
        result.Should().Be(guid);
        exitCode().Should().Be(0);
    }

    [Fact]
    public async Task ResolveRole_NameMatch_CaseInsensitive()
    {
        var handler = new FakeHandler(_ => RolesJson(
            ("aaaa-bbbb-cccc", "read-only"),
            ("dddd-eeee-ffff", "admin")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveRoleIdAsync(http, "Read-Only", ctx, CancellationToken.None);

        output.WriteLine($"Input: Read-Only => Result: {result}");
        result.Should().Be("aaaa-bbbb-cccc");
        exitCode().Should().Be(0);
    }

    [Fact]
    public async Task ResolveRole_NotFound_WritesErrorAndReturnsNull()
    {
        var handler = new FakeHandler(_ => RolesJson(
            ("aaaa-bbbb-cccc", "read-only")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveRoleIdAsync(http, "nonexistent", ctx, CancellationToken.None);

        output.WriteLine($"Input: nonexistent => Result: {result}");
        result.Should().BeNull();
        exitCode().Should().Be(1);
    }

    [Fact]
    public async Task ResolveRole_AmbiguousMatch_WritesErrorAndReturnsNull()
    {
        var handler = new FakeHandler(_ => RolesJson(
            ("aaaa-bbbb-cccc", "admin"),
            ("dddd-eeee-ffff", "admin")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var (ctx, exitCode) = CreateContext();
        var result = await NameResolver.ResolveRoleIdAsync(http, "admin", ctx, CancellationToken.None);

        output.WriteLine($"Input: admin (ambiguous) => Result: {result}");
        result.Should().BeNull();
        exitCode().Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a System.CommandLine <see cref="InvocationContext"/> backed by
    /// a minimal root command. Returns the context and a lambda that reads
    /// the exit code (since <c>ctx.ExitCode</c> is write-only in some versions
    /// of System.CommandLine, the lambda captures the mutable int).
    /// </summary>
    private static (InvocationContext ctx, Func<int> exitCode) CreateContext()
    {
        var root = new RootCommand();
        var result = root.Parse([]);
        var ctx = new InvocationContext(result);
        return (ctx, () => ctx.ExitCode);
    }

    /// <summary>
    /// Builds a JSON string matching <c>ClusterListResponse</c>.
    /// </summary>
    private static string ClustersJson(params (string id, string name)[] clusters)
    {
        var items = clusters.Select(c =>
            $$$"""{"id":"{{{c.id}}}","name":"{{{c.name}}}","description":"","status":"Online","registeredAt":"2026-01-01T00:00:00Z","labels":{}}""");
        return $$$"""{"clusters":[{{{string.Join(",", items)}}}]}""";
    }

    /// <summary>
    /// Builds a JSON string matching <c>RoleListResponse</c>.
    /// </summary>
    private static string RolesJson(params (string id, string name)[] roles)
    {
        var items = roles.Select(r =>
            $$"""{"id":"{{r.id}}","name":"{{r.name}}","description":"","kubernetesGroups":[],"createdAt":"2026-01-01T00:00:00Z"}""");
        return $$"""{"roles":[{{string.Join(",", items)}}]}""";
    }

    /// <summary>
    /// A minimal <see cref="HttpMessageHandler"/> that returns a canned JSON
    /// response (or throws) for every request. Used instead of an
    /// <see cref="System.Net.HttpListener"/> because the resolver only
    /// makes a single GET and we don't need a real network listener.
    /// </summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, string> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var json = responder(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
