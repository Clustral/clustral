using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Clustral.E2E.Tests.Fixtures;

/// <summary>
/// Typed REST API wrapper for the ControlPlane. Hides token acquisition,
/// request shaping, and JSON deserialization so that test methods can read
/// like high-level scenarios:
///
/// <code>
/// var (clusterId, bootstrap) = await cp.RegisterClusterAsync("e2e-cluster");
/// var token = await cp.IssueKubeconfigCredentialAsync(clusterId);
/// var response = await cp.KubectlGetAsync(clusterId, token, "/api/v1/namespaces");
/// </code>
/// </summary>
public sealed class ControlPlaneClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly KeycloakTokenClient _tokens;
    private string? _cachedToken;

    public ControlPlaneClient(Uri controlPlaneBaseUrl, KeycloakTokenClient tokens)
    {
        _http = new HttpClient { BaseAddress = controlPlaneBaseUrl };
        _tokens = tokens;
    }

    /// <summary>The base address used by this client (e.g. http://localhost:5100).</summary>
    public Uri BaseAddress => _http.BaseAddress!;

    /// <summary>Authenticates as the given user. Subsequent requests use this user's JWT.</summary>
    public async Task SignInAsync(string username = "admin", string password = "admin", CancellationToken ct = default)
    {
        _cachedToken = await _tokens.GetAccessTokenAsync(username, password, ct);
    }

    // ─── Clusters ────────────────────────────────────────────────────────────

    public async Task<RegisterClusterResponse> RegisterClusterAsync(
        string name, string description = "", CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Post, "api/v1/clusters", ct);
        request.Content = JsonContent.Create(new { name, description });

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);

        return await response.Content.ReadFromJsonAsync<RegisterClusterResponse>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty cluster registration response");
    }

    public async Task DeleteClusterAsync(Guid clusterId, CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Delete, $"api/v1/clusters/{clusterId}", ct);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);
    }

    public async Task<ClusterDto> GetClusterAsync(Guid clusterId, CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Get, $"api/v1/clusters/{clusterId}", ct);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);

        return await response.Content.ReadFromJsonAsync<ClusterDto>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty cluster response");
    }

    /// <summary>
    /// Polls <c>GET /api/v1/clusters/{id}</c> until the cluster reports the
    /// expected status, or throws if the timeout elapses.
    /// </summary>
    public async Task<ClusterDto> WaitForClusterStatusAsync(
        Guid clusterId, string expectedStatus, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        ClusterDto? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                last = await GetClusterAsync(clusterId, ct);
                if (string.Equals(last.Status, expectedStatus, StringComparison.OrdinalIgnoreCase))
                    return last;
            }
            catch (HttpRequestException)
            {
                // Transient — keep polling.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        throw new TimeoutException(
            $"Cluster {clusterId} did not reach status '{expectedStatus}' within {timeout}. Last seen: {last?.Status ?? "(unknown)"}");
    }

    // ─── Roles ───────────────────────────────────────────────────────────────

    public async Task<RoleDto> CreateRoleAsync(
        string name, IReadOnlyList<string> kubernetesGroups, string description = "", CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Post, "api/v1/roles", ct);
        request.Content = JsonContent.Create(new { name, description, kubernetesGroups });

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);

        return await response.Content.ReadFromJsonAsync<RoleDto>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty role response");
    }

    // ─── Users + assignments ─────────────────────────────────────────────────

    public async Task<UserProfileDto> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Get, "api/v1/users/me", ct);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);

        return await response.Content.ReadFromJsonAsync<UserProfileDto>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty user profile response");
    }

    public async Task AssignRoleAsync(Guid userId, Guid roleId, Guid clusterId, CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Post, $"api/v1/users/{userId}/assignments", ct);
        request.Content = JsonContent.Create(new { roleId, clusterId });

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);
    }

    // ─── Auth / credentials ──────────────────────────────────────────────────

    public async Task<IssueCredentialDto> IssueKubeconfigCredentialAsync(
        Guid clusterId, string? requestedTtl = null, CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Post, "api/v1/auth/kubeconfig-credential", ct);
        request.Content = JsonContent.Create(new { clusterId, requestedTtl });

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);

        return await response.Content.ReadFromJsonAsync<IssueCredentialDto>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty credential response");
    }

    public async Task RevokeCredentialAsync(Guid credentialId, CancellationToken ct = default)
    {
        var request = await BuildAuthenticatedRequest(HttpMethod.Delete, $"api/v1/auth/credentials/{credentialId}", ct);
        request.Content = JsonContent.Create(new { reason = "e2e-test" });

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, ct);
    }

    // ─── kubectl proxy ───────────────────────────────────────────────────────

    /// <summary>
    /// Sends a kubectl-style request through <c>/api/proxy/{clusterId}/...</c>.
    /// Uses the supplied kubeconfig credential token (NOT the OIDC user JWT).
    /// </summary>
    public async Task<HttpResponseMessage> KubectlSendAsync(
        HttpMethod method, Guid clusterId, string credentialToken, string k8sPath,
        HttpContent? body = null, CancellationToken ct = default)
    {
        var path = $"api/proxy/{clusterId}{(k8sPath.StartsWith('/') ? k8sPath : "/" + k8sPath)}";
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", credentialToken) },
        };
        if (body is not null) request.Content = body;

        return await _http.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> KubectlGetAsync(
        Guid clusterId, string credentialToken, string k8sPath, CancellationToken ct = default) =>
        KubectlSendAsync(HttpMethod.Get, clusterId, credentialToken, k8sPath, null, ct);

    // ─── Internals ───────────────────────────────────────────────────────────

    private async Task<HttpRequestMessage> BuildAuthenticatedRequest(
        HttpMethod method, string path, CancellationToken ct)
    {
        _cachedToken ??= await _tokens.GetAccessTokenAsync(ct: ct);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
        return request;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"ControlPlane request failed: {(int)response.StatusCode} {response.ReasonPhrase} — {body}",
            null,
            response.StatusCode);
    }

    public void Dispose() => _http.Dispose();
}

// ─── Wire DTOs (mirror ControlPlane API models) ──────────────────────────────

public sealed record RegisterClusterResponse(
    [property: JsonPropertyName("clusterId")] Guid ClusterId,
    [property: JsonPropertyName("bootstrapToken")] string BootstrapToken);

public sealed record ClusterDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("kubernetesVersion")] string? KubernetesVersion,
    [property: JsonPropertyName("agentVersion")] string? AgentVersion);

public sealed record RoleDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kubernetesGroups")] List<string> KubernetesGroups);

public sealed record IssueCredentialDto(
    [property: JsonPropertyName("credentialId")] Guid CredentialId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("issuedAt")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("subject")] string Subject);

public sealed record UserProfileDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email);
