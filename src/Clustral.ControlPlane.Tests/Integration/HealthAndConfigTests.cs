using System.Net;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class HealthAndConfigTests(
    ClustralWebApplicationFactory factory,
    ITestOutputHelper output)
{
    [Fact]
    public async Task Healthz_Returns200_NoAuth()
    {
        var client = factory.CreateClient(); // No auth header.
        var response = await client.GetAsync("/healthz");

        output.WriteLine($"GET /healthz => {(int)response.StatusCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }

    [Fact]
    public async Task Config_Returns200_NoAuth()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/config");

        output.WriteLine($"GET /api/v1/config => {(int)response.StatusCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("oidcAuthority", body);
    }
}
