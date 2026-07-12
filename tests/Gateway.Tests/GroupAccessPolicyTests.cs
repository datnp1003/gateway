using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Gateway.Infrastructure.Data;

namespace Gateway.Tests;

public class GroupAccessPolicyTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    public GroupAccessPolicyTests(WebApplicationFactory<Program> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid()}.db");
        _factory = factory;
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private HttpClient CreateClientWithIp(string fakeIp) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:DevBypass", "true");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GatewayDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<GatewayDbContext>(options =>
                    options.UseSqlite($"Data Source={_dbPath}"));

                services.AddSingleton<IStartupFilter>(
                    new FakeRemoteIpStartupFilter(IPAddress.Parse(fakeIp)));
            });
        }).CreateClient();

    private static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    /// <summary>Creates a group (with optional IP policies) + one endpoint, returns the public request path.</summary>
    private static async Task<(string RequestPath, string GroupId)> CreateGroupWithEndpoint(
        HttpClient client,
        string? groupBlocked = null, string? groupAllowed = null,
        string? epBlocked = null, string? epAllowed = null)
    {
        var slug = $"gpol-{Guid.NewGuid():N}"[..16];
        var groupResp = await client.PostAsync("/api/management/groups", Json(new
        {
            name = slug,
            path = slug,
            description = (string?)null,
            blockedIpRanges = groupBlocked,
            allowedIpRanges = groupAllowed
        }));
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
            requiresAuth = false,
            blockedIpRanges = epBlocked,
            allowedIpRanges = epAllowed
        }));
        Assert.Equal(HttpStatusCode.Created, epResp.StatusCode);

        return ($"/{slug}/svc/ping", groupId);
    }

    [Fact]
    public async Task GroupBlocklist_BlocksMatchingIp()
    {
        var client = CreateClientWithIp("9.9.9.9");
        var (path, _) = await CreateGroupWithEndpoint(client, groupBlocked: "9.9.9.9");
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GroupBlocklist_AllowsOtherIp()
    {
        var client = CreateClientWithIp("1.2.3.4");
        var (path, _) = await CreateGroupWithEndpoint(client, groupBlocked: "9.9.9.9");
        var resp = await client.GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GroupBlocklist_CidrRange_BlocksMatchingIp()
    {
        var client = CreateClientWithIp("10.5.6.7");
        var (path, _) = await CreateGroupWithEndpoint(client, groupBlocked: "10.0.0.0/8");
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GroupBlocklist_Ipv6Cidr_BlocksMatchingIp()
    {
        var client = CreateClientWithIp("2001:db8:1:2::5");
        var (path, _) = await CreateGroupWithEndpoint(client, groupBlocked: "2001:db8:1:2::/64");
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GroupBlocklist_Ipv6Cidr_AllowsOtherPrefix()
    {
        var client = CreateClientWithIp("2001:db8:ffff:0::5");
        var (path, _) = await CreateGroupWithEndpoint(client, groupBlocked: "2001:db8:1:2::/64");
        var resp = await client.GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GroupAllowlist_DeniesNonMatchingIp()
    {
        var client = CreateClientWithIp("9.9.9.9");
        var (path, _) = await CreateGroupWithEndpoint(client, groupAllowed: "10.0.0.0/8");
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GroupAllowlist_AllowsMatchingIp()
    {
        var client = CreateClientWithIp("10.1.2.3");
        var (path, _) = await CreateGroupWithEndpoint(client, groupAllowed: "10.0.0.0/8");
        var resp = await client.GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GroupAndEndpointAllowlists_BothMustMatch()
    {
        // IP passes the group allowlist but fails the endpoint allowlist → denied
        var client = CreateClientWithIp("9.9.9.9");
        var (path, _) = await CreateGroupWithEndpoint(
            client, groupAllowed: "9.0.0.0/8", epAllowed: "8.8.8.8");
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task EndpointBlocklist_StillApplies_WithGroupPolicyUnset()
    {
        var client = CreateClientWithIp("9.9.9.9");
        var (path, _) = await CreateGroupWithEndpoint(client, epBlocked: "9.9.9.9");
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateGroup_SetsAndClearsIpPolicies()
    {
        var client = CreateClientWithIp("1.2.3.4");
        var (_, groupId) = await CreateGroupWithEndpoint(client);

        // Set the blocklist via partial update (other fields omitted → unchanged)
        var putResp = await client.PutAsync($"/api/management/groups/{groupId}",
            Json(new { blockedIpRanges = "9.9.9.9" }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var getJson = await client.GetStringAsync($"/api/management/groups/{groupId}");
        var group = JsonDocument.Parse(getJson).RootElement;
        Assert.Equal("9.9.9.9", group.GetProperty("blockedIpRanges").GetString());

        // Empty string clears the policy
        putResp = await client.PutAsync($"/api/management/groups/{groupId}",
            Json(new { blockedIpRanges = "" }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        getJson = await client.GetStringAsync($"/api/management/groups/{groupId}");
        group = JsonDocument.Parse(getJson).RootElement;
        Assert.True(group.GetProperty("blockedIpRanges").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AuthEndpoints_AreNotSubjectToProxyAccessPolicies()
    {
        // Guarantee a seeded proxy endpoint matching /api/auth/** regardless of
        // the local (gitignored) appsettings.json contents.
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:DevBypass", "true");
            builder.UseSetting("ReverseProxy:Routes:authseed:ClusterId", "authseed-cluster");
            builder.UseSetting("ReverseProxy:Routes:authseed:Match:Path", "/api/auth/{**catch-all}");
            builder.UseSetting("ReverseProxy:Clusters:authseed-cluster:Destinations:d1:Address",
                "http://localhost:59996/");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GatewayDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<GatewayDbContext>(options =>
                    options.UseSqlite($"Data Source={_dbPath}"));

                services.AddSingleton<IStartupFilter>(
                    new FakeRemoteIpStartupFilter(IPAddress.Parse("9.9.9.9")));
            });
        }).CreateClient();

        // Block the client IP on the entire seeded "api" group.
        var groupsJson = await client.GetStringAsync("/api/management/groups");
        var apiGroupId = JsonDocument.Parse(groupsJson).RootElement.EnumerateArray()
            .Single(g => g.GetProperty("path").GetString() == "api")
            .GetProperty("id").GetString();
        var putResp = await client.PutAsync($"/api/management/groups/{apiGroupId}",
            Json(new { blockedIpRanges = "9.9.9.9" }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        // The dashboard login flow must stay reachable, or an admin who
        // misconfigures a policy locks themselves out permanently.
        var challenge = await client.GetAsync("/api/auth/challenge?returnUrl=/");
        Assert.Equal(HttpStatusCode.OK, challenge.StatusCode);
    }

    /// <summary>Prepends middleware that stamps a fake client IP (TestServer leaves it null).</summary>
    private sealed class FakeRemoteIpStartupFilter : IStartupFilter
    {
        private readonly IPAddress _ip;
        public FakeRemoteIpStartupFilter(IPAddress ip) => _ip = ip;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (ctx, nxt) =>
                {
                    ctx.Connection.RemoteIpAddress = _ip;
                    await nxt();
                });
                next(app);
            };
    }
}
