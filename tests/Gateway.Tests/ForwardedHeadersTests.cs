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

/// <summary>
/// Behind a reverse proxy the gateway must resolve the real client IP from
/// X-Forwarded-For — but only when the direct sender is an explicitly trusted
/// proxy. Untrusted senders must never be able to spoof their IP.
/// </summary>
public class ForwardedHeadersTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    public ForwardedHeadersTests(WebApplicationFactory<Program> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid()}.db");
        _factory = factory;
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private HttpClient CreateClient(string connectionIp, params (string Key, string Value)[] settings) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:DevBypass", "true");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GatewayDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<GatewayDbContext>(options =>
                    options.UseSqlite($"Data Source={_dbPath}"));

                services.AddSingleton<IStartupFilter>(
                    new FakeConnectionIpStartupFilter(IPAddress.Parse(connectionIp)));
            });
        }).CreateClient();

    private static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    /// <summary>Creates a group + endpoint whose blocklist contains 9.9.9.9; returns the proxy path.</summary>
    private static async Task<string> CreateEndpointBlocking9999(HttpClient client)
    {
        var slug = $"fwd-{Guid.NewGuid():N}"[..16];
        var groupResp = await client.PostAsync("/api/management/groups", Json(new
        {
            name = slug,
            path = slug,
            description = (string?)null,
            blockedIpRanges = "9.9.9.9"
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
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.Created, epResp.StatusCode);
        return $"/{slug}/svc/ping";
    }

    [Fact]
    public async Task TrustedProxy_ForwardedFor_IsHonoredByIpPolicy()
    {
        // Connection comes from the trusted proxy; the real (blocked) client
        // is in X-Forwarded-For and must be the IP the policy sees.
        var client = CreateClient("10.0.0.1",
            ("ForwardedHeaders:TrustedProxies:0", "10.0.0.1"));
        var path = await CreateEndpointBlocking9999(client);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "9.9.9.9");
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task TrustedNetwork_ForwardedFor_IsHonoredByIpPolicy()
    {
        var client = CreateClient("10.20.30.40",
            ("ForwardedHeaders:TrustedNetworks:0", "10.0.0.0/8"));
        var path = await CreateEndpointBlocking9999(client);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "9.9.9.9");
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UntrustedSender_ForwardedFor_IsIgnored()
    {
        // 1.2.3.4 is not a trusted proxy: its X-Forwarded-For must not let it
        // impersonate (or be punished as) another client.
        var client = CreateClient("1.2.3.4",
            ("ForwardedHeaders:TrustedProxies:0", "10.0.0.1"));
        var path = await CreateEndpointBlocking9999(client);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "9.9.9.9");
        var resp = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NoTrustConfigured_ForwardedFor_IsIgnored()
    {
        var client = CreateClient("1.2.3.4");
        var path = await CreateEndpointBlocking9999(client);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "9.9.9.9");
        var resp = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private sealed class FakeConnectionIpStartupFilter : IStartupFilter
    {
        private readonly IPAddress _ip;
        public FakeConnectionIpStartupFilter(IPAddress ip) => _ip = ip;

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
