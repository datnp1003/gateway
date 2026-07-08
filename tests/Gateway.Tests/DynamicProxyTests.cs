using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Gateway.Infrastructure.Data;

namespace Gateway.Tests;

public class DynamicProxyTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    public DynamicProxyTests(WebApplicationFactory<Program> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid()}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GatewayDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<GatewayDbContext>(options =>
                    options.UseSqlite($"Data Source={_dbPath}"));
            });
        });
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
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
    public async Task GroupsApi_WithSeed_IncludesAuthCluster()
    {
        var client = CreateClient();
        // Force seed by hitting the endpoint (EnsureCreated + Seed runs on first request through pipeline)
        var response = await client.GetAsync("/api/management/groups");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("api", json);
        Assert.Contains("/auth/", json);
    }

    [Fact]
    public async Task EndpointsApi_WithSeed_IncludesData()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/management/endpoints");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/auth/{**catch-all}", json);
        Assert.Contains("/learning/{**catch-all}", json);
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
        var client = CreateClient();
        var response = await client.GetAsync("/api/learning/courses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicAuthRoute_ForwardsToBackend()
    {
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
    public async Task CreateAndListGroup_WorksDynamically()
    {
        var client = CreateClient();
        var groupName = $"test-{Guid.NewGuid():N}"[..16];
        var payload = Json(new { name = groupName, path = groupName, description = (string?)null });
        var createResponse = await client.PostAsync("/api/management/groups", payload);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/management/groups");
        var json = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(groupName, json);
    }

    [Fact]
    public async Task CreateEndpoint_ThenDashboard_HasNewRoute()
    {
        var client = CreateClient();
        var groupName = $"/api/epg-{Guid.NewGuid():N}"[..20];

        var groupPayload = Json(new { name = groupName, path = groupName, description = (string?)null });
        var groupResp = await client.PostAsync("/api/management/groups", groupPayload);
        var groupJson = await groupResp.Content.ReadAsStringAsync();
        var groupId = JsonDocument.Parse(groupJson).RootElement.GetProperty("id").GetString()!;

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

        var dashResp = await client.GetAsync("/api/management/dashboard");
        var dashJson = await dashResp.Content.ReadAsStringAsync();
        Assert.Contains("/api/test/", dashJson);
    }
}
