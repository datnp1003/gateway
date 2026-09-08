using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Gateway.Infrastructure.Data;

namespace Gateway.Tests;

/// <summary>
/// Management CRUD contract: deterministic 400/404/409 instead of generic 500,
/// and endpoint PUT must follow the same partial-update semantics as groups
/// (null = keep, empty/zero = clear) so API clients cannot wipe policies by accident.
/// </summary>
public class ManagementApiValidationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly TestDatabase _database = new();

    public ManagementApiValidationTests(WebApplicationFactory<Program> factory)
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
                    options.UseNpgsql(_database.ConnectionString));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _database.Dispose();
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    private static async Task<string> CreateGroup(HttpClient client, string slug)
    {
        var resp = await client.PostAsync("/api/management/groups",
            Json(new { name = slug, path = slug, description = (string?)null }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;
    }

    // ─── Delete-missing → 404 ───

    [Fact]
    public async Task DeleteGroup_Missing_Returns404()
    {
        var client = CreateClient();
        var resp = await client.DeleteAsync($"/api/management/groups/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteEndpoint_Missing_Returns404()
    {
        var client = CreateClient();
        var resp = await client.DeleteAsync($"/api/management/endpoints/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ─── Invalid payloads → 400 ───

    [Fact]
    public async Task CreateGroup_EmptyName_Returns400()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/api/management/groups",
            Json(new { name = "", path = $"vg-{Guid.NewGuid():N}"[..12], description = (string?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_PathWithWhitespace_Returns400()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/api/management/groups",
            Json(new { name = $"vg-{Guid.NewGuid():N}"[..12], path = "bad path", description = (string?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_DuplicatePath_Returns409()
    {
        var client = CreateClient();
        var slug = $"vg-{Guid.NewGuid():N}"[..16];
        await CreateGroup(client, slug);
        var resp = await client.PostAsync("/api/management/groups",
            Json(new { name = slug, path = slug, description = (string?)null }));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task CreateEndpoint_NonAbsoluteDestination_Returns400()
    {
        var client = CreateClient();
        var groupId = await CreateGroup(client, $"vg-{Guid.NewGuid():N}"[..16]);
        var resp = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = "svc",
            pathPattern = "/svc/{**catch-all}",
            destination = "not-a-url",
            removePrefix = (string?)null,
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateEndpoint_PathPatternWithoutLeadingSlash_Returns400()
    {
        var client = CreateClient();
        var groupId = await CreateGroup(client, $"vg-{Guid.NewGuid():N}"[..16]);
        var resp = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = "svc",
            pathPattern = "svc/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateEndpoint_NonPositiveRateLimit_Returns400()
    {
        var client = CreateClient();
        var groupId = await CreateGroup(client, $"vg-{Guid.NewGuid():N}"[..16]);
        var resp = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = "svc",
            pathPattern = "/svc/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false,
            rateLimitPerMinute = -5
        }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateEndpoint_UnknownGroup_Returns400()
    {
        var client = CreateClient();
        var resp = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId = Guid.NewGuid(),
            name = "svc",
            pathPattern = "/svc/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ─── Duplicate endpoints → 409 ───

    [Fact]
    public async Task CreateEndpoint_DuplicatePathInGroup_Returns409()
    {
        var client = CreateClient();
        var groupId = await CreateGroup(client, $"vg-{Guid.NewGuid():N}"[..16]);
        var payload = new
        {
            groupId,
            name = "svc",
            pathPattern = "/svc/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false
        };
        var first = await client.PostAsync("/api/management/endpoints", Json(payload));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var resp = await client.PostAsync("/api/management/endpoints", Json(payload));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var error = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetString();
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task UpdateEndpoint_DuplicatePathInGroup_Returns409()
    {
        var client = CreateClient();
        var groupId = await CreateGroup(client, $"vg-{Guid.NewGuid():N}"[..16]);
        var first = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = "svc-a",
            pathPattern = "/a/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = "svc-b",
            pathPattern = "/b/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false
        }));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondId = JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var resp = await client.PutAsync($"/api/management/endpoints/{secondId}",
            Json(new { pathPattern = "/a/{**catch-all}" }));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var error = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetString();
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ─── Endpoint PUT partial-update semantics ───

    private async Task<(string GroupId, string EndpointId)> CreateEndpointWithPolicies(HttpClient client)
    {
        var groupId = await CreateGroup(client, $"vg-{Guid.NewGuid():N}"[..16]);
        var resp = await client.PostAsync("/api/management/endpoints", Json(new
        {
            groupId,
            name = "svc",
            pathPattern = "/svc/{**catch-all}",
            destination = "http://localhost:59999/",
            removePrefix = (string?)null,
            requiresAuth = false,
            rateLimitPerMinute = 5,
            blockedIpRanges = "9.9.9.9",
            allowedIpRanges = "10.0.0.0/8"
        }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var endpointId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;
        return (groupId, endpointId);
    }

    private static async Task<JsonElement> GetEndpoint(HttpClient client, string groupId, string endpointId)
    {
        var json = await client.GetStringAsync($"/api/management/endpoints?groupId={groupId}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == endpointId)
            .Clone();
    }

    [Fact]
    public async Task UpdateEndpoint_OmittedPolicyFields_AreRetained()
    {
        var client = CreateClient();
        var (groupId, endpointId) = await CreateEndpointWithPolicies(client);

        // Rename only: every omitted (null) policy field must stay untouched.
        var putResp = await client.PutAsync($"/api/management/endpoints/{endpointId}",
            Json(new { name = "renamed" }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var ep = await GetEndpoint(client, groupId, endpointId);
        Assert.Equal("renamed", ep.GetProperty("name").GetString());
        Assert.Equal(5, ep.GetProperty("rateLimitPerMinute").GetInt32());
        Assert.Equal("9.9.9.9", ep.GetProperty("blockedIpRanges").GetString());
        Assert.Equal("10.0.0.0/8", ep.GetProperty("allowedIpRanges").GetString());
    }

    [Fact]
    public async Task UpdateEndpoint_EmptyAndZeroValues_ClearPolicies()
    {
        var client = CreateClient();
        var (groupId, endpointId) = await CreateEndpointWithPolicies(client);

        // Same clearing convention as groups: empty string / zero clears.
        var putResp = await client.PutAsync($"/api/management/endpoints/{endpointId}",
            Json(new { blockedIpRanges = "", allowedIpRanges = "", rateLimitPerMinute = 0 }));
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);

        var ep = await GetEndpoint(client, groupId, endpointId);
        Assert.Equal(JsonValueKind.Null, ep.GetProperty("blockedIpRanges").ValueKind);
        Assert.Equal(JsonValueKind.Null, ep.GetProperty("allowedIpRanges").ValueKind);
        Assert.Equal(JsonValueKind.Null, ep.GetProperty("rateLimitPerMinute").ValueKind);
    }

    [Fact]
    public async Task UpdateEndpoint_InvalidDestination_Returns400()
    {
        var client = CreateClient();
        var (_, endpointId) = await CreateEndpointWithPolicies(client);

        var putResp = await client.PutAsync($"/api/management/endpoints/{endpointId}",
            Json(new { destination = "ftp://example.com/x" }));
        Assert.Equal(HttpStatusCode.BadRequest, putResp.StatusCode);
    }
}
