using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Gateway.Domain.Entities;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gateway.Tests;

public sealed class OperationsEventsHttpTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public OperationsEventsHttpTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            _database.Configure(builder);
            builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
            builder.UseSetting("Authentication:DevBypass", "true");
            builder.UseSetting("Authentication:Jwt:Secret", "unit-test-signing-secret-at-least-32-chars");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new FixedRemoteIpStartupFilter(IPAddress.Loopback)));
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task Events_HttpValidatesCursorsFiltersOrderingAndNonFrozenLateWrites()
    {
        var endpoint = Guid.NewGuid();
        var group = Guid.NewGuid();
        var at = DateTime.UtcNow.AddMinutes(-5).AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond));
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            db.ProxyRequestEvents.AddRange(
                Event(at, endpoint, group, "alpha", "upstream_response", 403),
                Event(at, endpoint, group, "beta", "upstream_response", 429),
                Event(at, endpoint, group, "gamma", "upstream_response", 503),
                Event(at.AddSeconds(-1), endpoint, group, "needle", "network_failure", null),
                Event(at.AddSeconds(-2), endpoint, group, "ok", "upstream_response", 200));
            await db.SaveChangesAsync();
        }

        var from = Uri.EscapeDataString(at.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(at.AddMinutes(1).ToString("O"));
        var baseUrl = $"/api/management/operations/events?from={from}&to={to}";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(baseUrl + "&cursor=not-base64")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(baseUrl + "&limit=101")).StatusCode);

        var first = await JsonAsync(baseUrl + "&limit=2");
        var cursor = first.GetProperty("nextCursor").GetString()!;
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(baseUrl + "&status=403&cursor=" + Uri.EscapeDataString(cursor))).StatusCode);
        Assert.Single((await JsonAsync(baseUrl + "&status=403")).GetProperty("items").EnumerateArray());
        Assert.Single((await JsonAsync(baseUrl + "&search=needle")).GetProperty("items").EnumerateArray());
        Assert.Equal(4, (await JsonAsync(baseUrl + "&failureOnly=true")).GetProperty("items").GetArrayLength());
        Assert.Single((await JsonAsync(baseUrl + "&outcome=network_failure")).GetProperty("items").EnumerateArray());

        await using (var db = CreateDb())
        {
            db.ProxyRequestEvents.Add(Event(at.AddMilliseconds(-500), endpoint, group, "late", "upstream_response", 500));
            await db.SaveChangesAsync();
        }
        var second = await JsonAsync(baseUrl + "&limit=2&cursor=" + Uri.EscapeDataString(cursor));
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("late database inserts", second.GetProperty("consistency").GetString());
    }

    [Fact]
    public async Task SelectedProxyRoute_CapturesGatewayAndUpstreamProvenanceOverHttp()
    {
        await using var upstream = await StartUpstreamAsync();
        var destination = upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var slug = "probe" + Guid.NewGuid().ToString("N")[..10];
        var group = await CreateAsync("/api/management/groups", new { name = slug, path = slug });
        var groupId = group.GetProperty("id").GetString()!;
        var endpoint = await CreateAsync("/api/management/endpoints", new { groupId, name = "probe", pathPattern = "/probe/{**catch-all}", destination, requiresAuth = false });
        var endpointId = endpoint.GetProperty("id").GetString()!;
        var path = $"/{slug}/probe";

        foreach (var status in new[] { 403, 429, 503 })
            Assert.Equal((HttpStatusCode)status, (await _client.GetAsync($"{path}/status/{status}")).StatusCode);

        await UpdateEndpointAsync(endpointId, new { blockedIpRanges = "127.0.0.1" });
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync($"{path}/status/200")).StatusCode);
        await UpdateEndpointAsync(endpointId, new { blockedIpRanges = "", rateLimitPerMinute = 1 });
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{path}/status/200")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _client.GetAsync($"{path}/status/200")).StatusCode);
        await UpdateEndpointAsync(endpointId, new { rateLimitPerMinute = 0 });
        var globalStatuses = await Task.WhenAll(Enumerable.Range(0, 550).Select(async _ =>
            (await _client.GetAsync($"{path}/status/200")).StatusCode));
        Assert.Contains(HttpStatusCode.TooManyRequests, globalStatuses);
        await UpdateEndpointAsync(endpointId, new { destination = "http://127.0.0.1:1" });
        Assert.Equal(HttpStatusCode.BadGateway, (await _client.GetAsync($"{path}/status/200")).StatusCode);

        await UpdateEndpointAsync(endpointId, new { destination });
        using (var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GetAsync($"{path}/wait", cancelled.Token));

        for (var attempts = 0; attempts < 100; attempts++)
        {
            await Task.Delay(50);
            await using var db = CreateDb();
            var events = await db.ProxyRequestEvents.Where(item => item.EndpointId == Guid.Parse(endpointId)).ToListAsync();
            if (events.Any(item => item.Outcome == "network_failure") && events.Any(item => item.Outcome == "client_disconnected"))
            {
                Assert.Contains(events, item => item.Outcome == "upstream_response" && item.ResponseStatus == 403);
                Assert.Contains(events, item => item.Outcome == "upstream_response" && item.ResponseStatus == 429);
                Assert.Contains(events, item => item.Outcome == "upstream_response" && item.ResponseStatus == 503);
                Assert.Contains(events, item => item.Outcome == "gateway_rejected" && item.ResponseStatus == 403);
                Assert.Contains(events, item => item.Outcome == "gateway_rejected" && item.ResponseStatus == 429);
                return;
            }
        }
        await using var finalDb = CreateDb();
        var captured = await finalDb.ProxyRequestEvents.Where(item => item.EndpointId == Guid.Parse(endpointId)).Select(item => item.Outcome + ":" + item.ResponseStatus).ToListAsync();
        throw new Xunit.Sdk.XunitException("Timed out waiting for captured network failure and client disconnect events: " + string.Join(", ", captured));
    }

    [Fact]
    public async Task CatchAllRoute_CapturesSafeOriginalPathsAndKeepsRecentEndpointsSeparate()
    {
        await using var upstream = await StartUpstreamAsync();
        var destination = upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var slug = "paths" + Guid.NewGuid().ToString("N")[..10];
        var group = await CreateAsync("/api/management/groups", new { name = slug, path = slug });
        var groupId = group.GetProperty("id").GetString()!;
        var endpoint = await CreateAsync("/api/management/endpoints", new { groupId, name = "paths", pathPattern = "/v1/{**catch-all}", destination, requiresAuth = false });
        var endpointId = endpoint.GetProperty("id").GetString()!;
        var prefix = $"/{slug}/v1";

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{prefix}/chat/completions?api_key=query-secret")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"{prefix}/models", new StringContent(""))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{prefix}/access_token/path-secret")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{prefix}/{new string('x', 600)}")).StatusCode);

        List<ProxyRequestEvent> captured = [];
        for (var attempts = 0; attempts < 100; attempts++)
        {
            await Task.Delay(50);
            await using var db = CreateDb();
            captured = await db.ProxyRequestEvents.Where(item => item.EndpointId == Guid.Parse(endpointId)).ToListAsync();
            if (captured.Count >= 4) break;
        }

        Assert.Contains(captured, item => item.Method == "GET" && item.RequestPath == $"{prefix}/chat/completions");
        Assert.Contains(captured, item => item.Method == "POST" && item.RequestPath == $"{prefix}/models");
        Assert.Contains(captured, item => item.RequestPath == $"{prefix}/access_token/[redacted]");
        Assert.DoesNotContain(captured, item => item.RequestPath.Contains("query-secret") || item.RequestPath.Contains("path-secret"));
        Assert.Contains(captured, item => item.RequestPath.StartsWith(prefix + "/", StringComparison.Ordinal) && item.RequestPath.Length <= 512);

        await using (var db = CreateDb())
        {
            var legacy = Event(DateTime.UtcNow, Guid.Parse(endpointId), Guid.Parse(groupId), "paths", "upstream_response", 200);
            legacy.RequestPath = $"{prefix}/{{**catch-all}}";
            db.ProxyRequestEvents.Add(legacy);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            var recent = (await new ProxyOperationsQueryService(db).GetOverviewAsync(DateTime.UtcNow, CancellationToken.None)).RecentEndpoints;
            Assert.Contains(recent, item => item.EndpointId == Guid.Parse(endpointId) && item.Method == "GET" && item.RequestPath == $"{prefix}/chat/completions");
            Assert.Contains(recent, item => item.EndpointId == Guid.Parse(endpointId) && item.Method == "POST" && item.RequestPath == $"{prefix}/models");
            Assert.Contains(recent, item => item.EndpointId == Guid.Parse(endpointId) && item.RequestPath == $"{prefix}/{{**catch-all}}");
        }

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        var logs = await JsonAsync($"/api/management/operations/events?from={from}&to={to}&endpointId={endpointId}&search={Uri.EscapeDataString($"{prefix}/chat/completions")}");
        Assert.Contains(logs.GetProperty("items").EnumerateArray(), item => item.GetProperty("requestPath").GetString() == $"{prefix}/chat/completions");
    }

    private GatewayDbContext CreateDb() => new(new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(_database.ConnectionString).Options);

    private static ProxyRequestEvent Event(DateTime occurredAt, Guid endpoint, Guid group, string name, string outcome, int? status) => new()
    {
        Id = Guid.NewGuid(), OccurredAt = occurredAt, CompletedAt = occurredAt, DurationMs = 10, Method = "GET", RequestPath = "/safe/{**path}", EndpointId = endpoint, GroupId = group,
        EndpointName = name, GroupName = "group", ConfiguredDestination = "http://backend", Outcome = outcome, ResponseStatus = status
    };

    private async Task<JsonElement> JsonAsync(string url)
    {
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<JsonElement> CreateAsync(string path, object body)
    {
        using var response = await _client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task UpdateEndpointAsync(string endpointId, object body)
    {
        using var response = await _client.PutAsJsonAsync($"/api/management/endpoints/{endpointId}", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<WebApplication> StartUpstreamAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/probe/status/{status:int}", (int status) => Results.StatusCode(status));
        app.MapGet("/probe/wait", async context => await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted));
        app.Map("/{**path}", () => Results.Ok());
        await app.StartAsync();
        return app;
    }

    private sealed class FixedRemoteIpStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.RemoteIpAddress = address;
                await following();
            });
            next(app);
        };
    }
}
