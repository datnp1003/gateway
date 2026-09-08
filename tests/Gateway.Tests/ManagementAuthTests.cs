using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Gateway.Authentication;
using Gateway.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Tests;

public class ManagementAuthTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string JwtSecret = "unit-test-signing-secret-at-least-32-chars";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly TestDatabase _database = new();

    public ManagementAuthTests(WebApplicationFactory<Program> factory)
    {

        _factory = factory.WithWebHostBuilder(_ => { });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _database.Dispose();
    }

    private WebApplicationFactory<Program> CreateFactory(bool devBypass, bool fakeExternalAuth = false) =>
        _factory.WithWebHostBuilder(builder =>
        {
            _database.Configure(builder);
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
                    options.UseNpgsql(_database.ConnectionString));

                if (fakeExternalAuth)
                {
                    // Swap the External cookie handler for a header-driven fake so
                    // /api/auth/google/callback can be exercised without Google.
                    services.AddTransient<FakeExternalAuthHandler>();
                    services.PostConfigure<AuthenticationOptions>(options =>
                        options.Schemes.First(s => s.Name == ManagementAuth.ExternalScheme)
                            .HandlerType = typeof(FakeExternalAuthHandler));
                }
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

    // returnUrl is an exact whitelist of the SPA dashboard shell ("/"): API,
    // health, proxy routes, protocol-relative/backslash tricks and absolute
    // URLs must all collapse to "/" so a one-time code can never be delivered
    // to a non-dashboard surface.
    [Theory]
    [InlineData("/api")]
    [InlineData("/api/auth/exchange")]
    [InlineData("/health")]
    [InlineData("/assets/index-BfipZjEm.js")]
    [InlineData("/api/learning/courses")] // reverse-proxy route
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example/phish")]
    public async Task Challenge_NonDashboardReturnUrl_FallsBackToRoot(string returnUrl)
    {
        var client = CreateClient(devBypass: true);
        var response = await client.GetAsync(
            "/api/auth/challenge?returnUrl=" + Uri.EscapeDataString(returnUrl));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("/api/auth/login?returnUrl=%2F", doc.RootElement.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/auth/exchange")]
    [InlineData("/health")]
    [InlineData("/assets/index-BfipZjEm.js")]
    [InlineData("/api/learning/courses")] // reverse-proxy route
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example/phish")]
    public async Task Login_NonDashboardReturnUrl_RedirectsCodeToRoot(string returnUrl)
    {
        var client = CreateClient(devBypass: true);
        var login = await client.GetAsync(
            "/api/auth/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("/?auth=callback&code=", login.Headers.Location!.ToString());
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

    // ── Google callback (External scheme faked; no real OAuth round-trip) ──

    [Fact]
    public async Task GoogleCallback_NonAllowlistedEmail_RedirectsDeniedWithoutEmail()
    {
        var client = CreateFactory(devBypass: false, fakeExternalAuth: true).CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(FakeExternalAuthHandler.EmailHeader, "intruder@example.com");

        var response = await client.GetAsync("/api/auth/google/callback?returnUrl=%2F");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Exactly auth=denied — the rejected email must not leak into the URL.
        Assert.Equal("/?auth=denied", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task GoogleCallback_AllowlistedEmail_HostileReturnUrl_RedirectsCodeToRoot()
    {
        var client = CreateFactory(devBypass: false, fakeExternalAuth: true).CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(FakeExternalAuthHandler.EmailHeader, "admin@example.com");

        var response = await client.GetAsync(
            "/api/auth/google/callback?returnUrl=" + Uri.EscapeDataString("/api/management/routes"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/?auth=callback&code=", response.Headers.Location!.ToString());
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

/// <summary>
/// Stand-in for the External cookie scheme: authenticates as whatever email the
/// X-Test-External-Email request header carries (NoResult when absent), so the
/// Google callback endpoint can be driven without the real OAuth round-trip.
/// </summary>
public sealed class FakeExternalAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : SignOutAuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string EmailHeader = "X-Test-External-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Request.Headers[EmailHeader].ToString();
        if (string.IsNullOrEmpty(email))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.Name, "Test User")],
            Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) =>
        Task.CompletedTask;
}
