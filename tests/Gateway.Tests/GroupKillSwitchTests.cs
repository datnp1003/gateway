using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Gateway.Infrastructure.Data;

namespace Gateway.Tests;

/// <summary>
/// Group is a parent kill-switch: disabling a group must remove every child
/// endpoint route from YARP, even when the endpoint itself stays enabled.
/// Management APIs must still list disabled groups/endpoints for editing.
/// </summary>
public class GroupKillSwitchTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    public GroupKillSwitchTests(WebApplicationFactory<Program> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid()}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:DevBypass", "true");
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

    private static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    /// <summary>Creates an enabled group + one enabled endpoint; returns ids and the public request path.</summary>
    private static async Task<(string GroupId, string EndpointId, string RequestPath)> CreateGroupWithEndpoint(
        HttpClient client)
    {
        var slug = $"kill-{Guid.NewGuid():N}"[..16];
        var groupResp = await client.PostAsync("/api/management/groups",
            Json(new { name = slug, path = slug, description = (string?)null }));
        Assert.Equal(HttpStatusCode.Created, groupResp.StatusCode);
        var groupId = JsonDocument.Parse(await groupResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var epResp = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = "svc",
            pathPattern = "/svc/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.Created, epResp.StatusCode);
        var endpointId = JsonDocument.Parse(await epResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        return (groupId, endpointId, $"/{slug}/svc/ping");
    }

    [Fact]
    public async Task DisableGroup_RemovesChildRoute_AndRequestIsNotProxied()
    {
        var client = _factory.CreateClient();
        var (groupId, endpointId, path) = await CreateGroupWithEndpoint(client);

        // Enabled: route registered and request reaches the proxy (backend missing → 502)
        var routesJson = await client.GetStringAsync("/api/management/routes");
        Assert.Contains($"route-{endpointId}", routesJson);
        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync(path)).StatusCode);

        // Disable the group — the endpoint itself stays enabled
        var putResp = await client.PutAsync($"/api/management/groups/{groupId}",
            Json(new { isEnabled = false }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        routesJson = await client.GetStringAsync("/api/management/routes");
        Assert.DoesNotContain($"route-{endpointId}", routesJson);
        var clustersJson = await client.GetStringAsync("/api/management/clusters");
        Assert.DoesNotContain($"cluster-{endpointId}", clustersJson);

        var blocked = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);

        // Management API still lists the disabled group and its endpoint for editing
        var groupsJson = await client.GetStringAsync("/api/management/groups");
        Assert.Contains(groupId, groupsJson);
        var endpointsJson = await client.GetStringAsync("/api/management/endpoints");
        Assert.Contains(endpointId, endpointsJson);
    }

    [Fact]
    public async Task ReEnableGroup_RestoresRoute_AndRequestReachesProxy()
    {
        var client = _factory.CreateClient();
        var (groupId, endpointId, path) = await CreateGroupWithEndpoint(client);

        var putResp = await client.PutAsync($"/api/management/groups/{groupId}",
            Json(new { isEnabled = false }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);

        putResp = await client.PutAsync($"/api/management/groups/{groupId}",
            Json(new { isEnabled = true }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var routesJson = await client.GetStringAsync("/api/management/routes");
        Assert.Contains($"route-{endpointId}", routesJson);
        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task DisabledEndpoint_InEnabledGroup_StaysDisabledIndependently()
    {
        var client = _factory.CreateClient();
        var (groupId, endpointId, path) = await CreateGroupWithEndpoint(client);

        var putResp = await client.PutAsync($"/api/management/endpoints/{endpointId}",
            Json(new { isEnabled = false }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var routesJson = await client.GetStringAsync("/api/management/routes");
        Assert.DoesNotContain($"route-{endpointId}", routesJson);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);

        // Group stays enabled and both remain visible via the management API
        var groupJson = await client.GetStringAsync($"/api/management/groups/{groupId}");
        Assert.True(JsonDocument.Parse(groupJson).RootElement.GetProperty("isEnabled").GetBoolean());
        var endpointsJson = await client.GetStringAsync($"/api/management/endpoints?groupId={groupId}");
        Assert.Contains(endpointId, endpointsJson);
    }
}
