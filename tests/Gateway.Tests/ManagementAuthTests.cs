using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gateway.Authentication;
using Gateway.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

public class ManagementAuthTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string JwtSecret = "unit-test-signing-secret-at-least-32-chars";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    public ManagementAuthTests(WebApplicationFactory<Program> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid()}.db");
        _factory = factory;
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private WebApplicationFactory<Program> CreateFactory(bool devBypass) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:DevBypass", devBypass ? "true" : "false");
            builder.UseSetting("Authentication:Jwt:Secret", JwtSecret);
            builder.UseSetting("Authentication:Jwt:Issuer", "Gateway.Admin");
            builder.UseSetting("Authentication:Jwt:Audience", "Gateway.Dashboard");
            if (!devBypass)
            {
                // Real Google creds are not needed: no test drives the actual
                // OAuth round-trip; bearer enforcement happens before it.
                builder.UseSetting("Authentication:Google:ClientId", "test-client-id");
                builder.UseSetting("Authentication:Google:ClientSecret", "test-client-secret");
                builder.UseSetting("Authentication:AllowedEmails", "admin@example.com");
            }
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GatewayDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<GatewayDbContext>(options =>
                    options.UseSqlite($"Data Source={_dbPath}"));
            });
        });

    private HttpClient CreateClient(bool devBypass) =>
        CreateFactory(devBypass).CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> LoginAndGetCodeAsync(HttpClient client, string returnUrl = "/")
    {
        var login = await client.GetAsync($"/api/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var location = login.Headers.Location!.ToString();
        Assert.StartsWith("/?auth=callback&code=", location);
        return location.Split("code=")[1];
    }

    private static async Task<JsonElement> ExchangeAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    // ── SPA shell / pass-through surfaces ──

    [Fact]
    public async Task DashboardRoot_WithoutAuth_ServesSpaShell()
    {
        var client = CreateClient(devBypass: false);
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Health_WithoutAuth_Returns200()
    {
        var client = CreateClient(devBypass: false);
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProxyRoute_WithoutAuth_StillPassesThrough()
    {
        var client = CreateClient(devBypass: false);
        // Seeded dynamic route: forwarded to backend (down in tests → 502), never 401/403.
        var response = await client.GetAsync("/api/learning/courses");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    // ── Management API enforcement ──

    [Fact]
    public async Task ManagementApi_WithoutBearer_Returns401()
    {
        var client = CreateClient(devBypass: false);
        var response = await client.GetAsync("/api/management/routes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagementApi_WithAllowlistedBearer_Returns200()
    {
        var factory = CreateFactory(devBypass: false);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = factory.Services.GetRequiredService<AdminTokenService>()
            .Issue("admin@example.com", "Admin", null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/management/routes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ManagementApi_WithNonAllowlistedBearer_Returns403()
    {
        var factory = CreateFactory(devBypass: false);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (token, _) = factory.Services.GetRequiredService<AdminTokenService>()
            .Issue("intruder@example.com", null, null);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/management/routes");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Dev bypass login flow: challenge → login → code → exchange → JWT ──

    [Fact]
    public async Task Challenge_ReturnsLoginUrl()
    {
        var client = CreateClient(devBypass: true);
        var response = await client.GetAsync("/api/auth/challenge?returnUrl=/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("/api/auth/login?returnUrl=%2F", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Challenge_SanitizesNonLocalReturnUrl()
    {
        var client = CreateClient(devBypass: true);
        var response = await client.GetAsync(
            "/api/auth/challenge?returnUrl=" + Uri.EscapeDataString("https://evil.example/phish"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("/api/auth/login?returnUrl=%2F", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task DevBypassLogin_RedirectsWithOneTimeCode()
    {
        var client = CreateClient(devBypass: true);
        var code = await LoginAndGetCodeAsync(client);
        Assert.False(string.IsNullOrWhiteSpace(code));
        // One-time code only — never a JWT in the URL.
        Assert.DoesNotContain(".", code);
    }

    [Fact]
    public async Task DevBypassLogin_SanitizesNonLocalReturnUrl()
    {
        var client = CreateClient(devBypass: true);
        var login = await client.GetAsync(
            "/api/auth/login?returnUrl=" + Uri.EscapeDataString("https://evil.example/phish"));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("/?auth=callback&code=", login.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Exchange_ReturnsAccessTokenAndUser()
    {
        var client = CreateClient(devBypass: true);
        var code = await LoginAndGetCodeAsync(client);
        var result = await ExchangeAsync(client, code);

        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("accessToken").GetString()));
        Assert.True(result.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal("dev@local", result.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Exchange_ReusedCode_Fails()
    {
        var client = CreateClient(devBypass: true);
        var code = await LoginAndGetCodeAsync(client);
        await ExchangeAsync(client, code);

        var second = await client.PostAsJsonAsync("/api/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Exchange_UnknownCode_Fails()
    {
        var client = CreateClient(devBypass: true);
        var response = await client.PostAsJsonAsync("/api/auth/exchange", new { code = "not-a-real-code" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── /api/auth/me is bearer-validated ──

    [Fact]
    public async Task AuthMe_WithoutBearer_Returns401()
    {
        var client = CreateClient(devBypass: false);
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("\"authenticated\":false", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AuthMe_WithBearer_ReturnsUser()
    {
        var client = CreateClient(devBypass: true);
        var code = await LoginAndGetCodeAsync(client);
        var result = await ExchangeAsync(client, code);
        var token = result.GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal("dev@local", doc.RootElement.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Logout_Returns204()
    {
        var client = CreateClient(devBypass: true);
        var response = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
