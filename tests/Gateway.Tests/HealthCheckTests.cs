using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.Tests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthyContentType()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/plain", response.Content.Headers.ContentType?.ToString() ?? "");
    }

    [Fact]
    public async Task AuthEndpoint_WithoutToken_ProxiesCorrectly()
    {
        // Auth endpoints are public (no AuthorizationPolicy)
        // Without a backend, YARP returns 502 Bad Gateway
        // This test verifies the route is configured
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/auth/login",
            new StringContent("{\"email\":\"test@test.com\"}",
                System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
