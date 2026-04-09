using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Clustral.E2E.Tests.Fixtures;

/// <summary>
/// Acquires JWT access tokens from Keycloak via the OAuth2 resource owner
/// password credentials grant. Used by E2E tests so they can call ControlPlane
/// REST endpoints with real OIDC tokens — no token mocking, no test auth bypass.
///
/// Requires the <c>clustral-cli</c> client to have <c>directAccessGrantsEnabled: true</c>.
/// </summary>
public sealed class KeycloakTokenClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _tokenEndpoint;
    private readonly string _clientId;

    public KeycloakTokenClient(Uri keycloakBaseUrl, string realm = "clustral", string clientId = "clustral-cli")
    {
        _http = new HttpClient();
        _tokenEndpoint = new Uri(keycloakBaseUrl, $"realms/{realm}/protocol/openid-connect/token").ToString();
        _clientId = clientId;
    }

    /// <summary>
    /// Exchanges username/password for an access token via the password grant.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(
        string username = "admin",
        string password = "admin",
        CancellationToken ct = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _clientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid email profile",
        });

        using var response = await _http.PostAsync(_tokenEndpoint, form, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Keycloak token request failed ({(int)response.StatusCode}): {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty token response from Keycloak");

        if (string.IsNullOrEmpty(payload.AccessToken))
            throw new InvalidOperationException("Keycloak returned an empty access_token");

        return payload.AccessToken;
    }

    public void Dispose() => _http.Dispose();

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);
}
