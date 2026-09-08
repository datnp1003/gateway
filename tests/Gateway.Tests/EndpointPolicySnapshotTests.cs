using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Gateway.Infrastructure.Data;

namespace Gateway.Tests;

/// <summary>
/// Access policies must be enforced from an in-memory snapshot: no database
/// query on the per-request proxy path, longest-prefix endpoint matching, and
/// snapshot refresh whenever CRUD triggers a YARP sync.
/// </summary>
public class EndpointPolicySnapshotTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly TestDatabase _database = new();
    private readonly CountingCommandInterceptor _dbCommands = new();

    public EndpointPolicySnapshotTests(WebApplicationFactory<Program> factory)
    {

        _factory = factory.WithWebHostBuilder(builder =>
        {
            _database.Configure(builder);
            builder.UseSetting("Authentication:DevBypass", "true");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GatewayDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<GatewayDbContext>(options =>
                    options.UseNpgsql(_database.ConnectionString)
                           .AddInterceptors(_dbCommands));

                services.AddSingleton<IStartupFilter>(
                    new FakeRemoteIpStartupFilter(IPAddress.Parse("9.9.9.9")));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _database.Dispose();
    }

    private static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    private static async Task<(string GroupId, string EndpointId)> CreateGroupWithEndpoint(
        HttpClient client, string slug, string pathPattern, string? blocked = null)
    {
        var groupResp = await client.PostAsync("/api/management/groups",
            Json(new { name = slug, path = slug, description = (string?)null }));
        Assert.Equal(HttpStatusCode.Created, groupResp.StatusCode);
        var groupId = JsonDocument.Parse(await groupResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;
        var endpointId = await AddEndpoint(client, groupId, pathPattern, blocked);
        return (groupId, endpointId);
    }

    private static async Task<string> AddEndpoint(
        HttpClient client, string groupId, string pathPattern, string? blocked)
    {
        var epResp = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = $"ep-{Guid.NewGuid():N}"[..12],
            pathPattern,
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false,
            blockedIpRanges = blocked
        }));
        Assert.Equal(HttpStatusCode.Created, epResp.StatusCode);
        return JsonDocument.Parse(await epResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task ProxyRequest_DoesNotQueryDatabase()
    {
        var client = _factory.CreateClient();
        var slug = $"snap-{Guid.NewGuid():N}"[..16];
        await CreateGroupWithEndpoint(client, slug, "/svc/{**catch-all}", blocked: "9.9.9.9");

        // Warm-up (policy is enforced), then measure a second request.
        var warmup = await client.GetAsync($"/{slug}/svc/ping");
        Assert.Equal(HttpStatusCode.Forbidden, warmup.StatusCode);

        _dbCommands.Reset();
        var resp = await client.GetAsync($"/{slug}/svc/ping");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(0, _dbCommands.Count);
    }

    [Fact]
    public async Task OverlappingRoutes_LongestPrefixPolicyWins()
    {
        var client = _factory.CreateClient();
        var slug = $"snap-{Guid.NewGuid():N}"[..16];
        // Broad endpoint (no policy) created FIRST so naive first-match picks it.
        var (groupId, _) = await CreateGroupWithEndpoint(client, slug, "/svc/{**catch-all}");
        await AddEndpoint(client, groupId, "/svc/special/{**catch-all}", blocked: "9.9.9.9");

        // The more specific endpoint's blocklist must apply on its subtree...
        var special = await client.GetAsync($"/{slug}/svc/special/thing");
        Assert.Equal(HttpStatusCode.Forbidden, special.StatusCode);

        // ...while the broad endpoint stays open elsewhere.
        var broad = await client.GetAsync($"/{slug}/svc/other");
        Assert.NotEqual(HttpStatusCode.Forbidden, broad.StatusCode);
    }

    [Fact]
    public async Task PolicyCrudUpdate_TakesEffect_WithoutRestart()
    {
        var client = _factory.CreateClient();
        var slug = $"snap-{Guid.NewGuid():N}"[..16];
        var (_, endpointId) = await CreateGroupWithEndpoint(client, slug, "/svc/{**catch-all}");

        var before = await client.GetAsync($"/{slug}/svc/ping");
        Assert.NotEqual(HttpStatusCode.Forbidden, before.StatusCode);

        var putResp = await client.PutAsync($"/api/management/endpoints/{endpointId}",
            Json(new { blockedIpRanges = "9.9.9.9" }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var after = await client.GetAsync($"/{slug}/svc/ping");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Volatile.Write(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

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
