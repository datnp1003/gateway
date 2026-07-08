using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

public class DynamicProxyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DynamicProxyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Let it use SQLite — EnsureCreated handles it
            });
        });
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ManagementHealth_ReturnsMetrics()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/management/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", json);
    }

    [Fact]
    public async Task GroupsApi_ListGroups_IncludesSeededData()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/management/groups");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("auth-cluster", json);
        Assert.Contains("learning-cluster", json);
    }

    [Fact]
    public async Task EndpointsApi_ListEndpoints_IncludesSeededData()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/management/endpoints");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/auth/", json);
    }

    [Fact]
    public async Task SyncEndpoint_ReturnsSuccess()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/api/management/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_ReturnsRoutesAndClusters()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/management/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("routes", json);
        Assert.Contains("clusters", json);
    }

    [Fact]
    public async Task ProtectedRoutes_WithoutJWT_Return401()
    {
        // Seeded routes from appsettings.json have auth on non-auth endpoints
        var client = CreateClient();
        var response = await client.GetAsync("/api/learning/courses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicAuthRoute_ForwardsToBackend()
    {
        // Auth route is public, YARP tries to proxy → 502 (no backend running)
        var client = CreateClient();
        var response = await client.GetAsync("/api/auth/register");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsNotFound()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/nonexistent/thing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_ThenList_ShowsNewGroup()
    {
        var client = CreateClient();
        var payload = Json(new { name = "test-group", description = (string?)null });
        var createResponse = await client.PostAsync("/api/management/groups", payload);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/management/groups");
        var json = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("test-group", json);
    }

    [Fact]
    public async Task CreateEndpoint_ThenDashboard_HasNewRoute()
    {
        var client = CreateClient();

        // Create a group first
        var groupPayload = Json(new { name = "ep-test-group", description = (string?)null });
        var groupResp = await client.PostAsync("/api/management/groups", groupPayload);
        var groupJson = await groupResp.Content.ReadAsStringAsync();
        var groupId = JsonDocument.Parse(groupJson).RootElement.GetProperty("id").GetString()!;

        // Create an endpoint
        var epPayload = Json(new
        {
            groupId,
            name = "test-ep",
            pathPattern = "/api/test/{**catch-all}",
            destination = "http://localhost:9999/",
            removePrefix = "/api/test",
            requiresAuth = false
        });
        var epResp = await client.PostAsync("/api/management/endpoints", epPayload);
        Assert.Equal(HttpStatusCode.Created, epResp.StatusCode);

        // Verify dashboard has the new route
        var dashResp = await client.GetAsync("/api/management/dashboard");
        var dashJson = await dashResp.Content.ReadAsStringAsync();
        Assert.Contains("/api/test/", dashJson);
    }
}
