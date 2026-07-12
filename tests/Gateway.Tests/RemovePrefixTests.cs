using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Gateway.Infrastructure.Data;

namespace Gateway.Tests;

/// <summary>
/// The path prefix stripped before forwarding must honor the endpoint's
/// RemovePrefix when set, and default to stripping only the group path.
/// Verified end-to-end against a real Kestrel echo backend.
/// </summary>
public class RemovePrefixTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    public RemovePrefixTests(WebApplicationFactory<Program> factory)
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

    /// <summary>Minimal Kestrel backend on a random port that echoes the path it received.</summary>
    private static async Task<(WebApplication App, string BaseUrl)> StartEchoBackendAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Run(async ctx => await ctx.Response.WriteAsync($"echo:{ctx.Request.Path.Value}"));
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return (app, app.Urls.First());
    }

    private async Task<string> CreateGroupAndEndpoint(
        HttpClient client, string slug, string destination, string? removePrefix)
    {
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
            destination,
            removePrefix,
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.Created, epResp.StatusCode);
        return groupId;
    }

    [Fact]
    public async Task ExplicitRemovePrefix_IsApplied_ToForwardedPath()
    {
        var (backend, baseUrl) = await StartEchoBackendAsync();
        await using var _ = backend;

        var client = _factory.CreateClient();
        var slug = $"rp-{Guid.NewGuid():N}"[..16];
        await CreateGroupAndEndpoint(client, slug, baseUrl + "/", removePrefix: $"/{slug}/svc");

        var resp = await client.GetAsync($"/{slug}/svc/hello/world");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("echo:/hello/world", body);
    }

    [Fact]
    public async Task NoRemovePrefix_DefaultsToStrippingGroupPath()
    {
        var (backend, baseUrl) = await StartEchoBackendAsync();
        await using var _ = backend;

        var client = _factory.CreateClient();
        var slug = $"rp-{Guid.NewGuid():N}"[..16];
        await CreateGroupAndEndpoint(client, slug, baseUrl + "/", removePrefix: null);

        var resp = await client.GetAsync($"/{slug}/svc/ping");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("echo:/svc/ping", body);
    }

    [Fact]
    public async Task Seed_PreservesPathRemovePrefixTransform()
    {
        // Inject a synthetic route with a PathRemovePrefix transform so the test
        // does not depend on the local (gitignored) appsettings.json contents.
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ReverseProxy:Routes:seedtest-route:ClusterId", "seedtest-cluster");
            builder.UseSetting("ReverseProxy:Routes:seedtest-route:Match:Path", "/api/seedtest/{**catch-all}");
            builder.UseSetting("ReverseProxy:Routes:seedtest-route:Transforms:0:PathRemovePrefix", "/api/seedtest");
            builder.UseSetting("ReverseProxy:Clusters:seedtest-cluster:Destinations:d1:Address", "http://localhost:59997/");
        });

        var client = factory.CreateClient();
        var json = await client.GetStringAsync("/api/management/endpoints");
        using var doc = JsonDocument.Parse(json);
        var seeded = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == "seedtest-route");
        Assert.Equal("/api/seedtest", seeded.GetProperty("removePrefix").GetString());
    }
}
