using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Gateway.Infrastructure.Data;

namespace Gateway.Tests;

/// <summary>
/// Production deployment contract: the app must fail closed when auth config is
/// missing, must accept the exact environment keys docker-compose injects, and
/// must persist SQLite at a configurable path so a container volume can hold it.
/// </summary>
public class ProductionConfigTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    public ProductionConfigTests(WebApplicationFactory<Program> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gateway-test-{Guid.NewGuid()}.db");
        _factory = factory;
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    /// <summary>
    /// Removes appsettings*.json so the test sees only the settings it supplies —
    /// local (gitignored) appsettings must not leak real values into assertions.
    /// </summary>
    private WebApplicationFactory<Program> CreateIsolatedFactory(
        string environment, Dictionary<string, string?> settings) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // UseSetting reaches configuration reads inside the Program body;
            // sources added via ConfigureAppConfiguration apply only at Build().
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                foreach (var source in cfg.Sources.OfType<JsonConfigurationSource>().ToList())
                    cfg.Sources.Remove(source);
            });
        });

    [Fact]
    public void Production_WithoutAuthConfig_FailsClosed()
    {
        var factory = CreateIsolatedFactory("Production", new()
        {
            ["Authentication:DevBypass"] = "false",
            ["Authentication:Jwt:Secret"] = "",
            ["Authentication:Google:ClientId"] = "",
            ["Authentication:Google:ClientSecret"] = "",
            ["Authentication:AllowedEmails"] = "",
            ["ConnectionStrings:Gateway"] = $"Data Source={_dbPath}",
        });

        // Host must never come up serving requests without a JWT secret.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Production_WithComposeShapedEnvKeys_BootsAndServesHealth()
    {
        // The exact env var names docker-compose.yml must inject.
        var envVars = new Dictionary<string, string?>
        {
            ["Authentication__Jwt__Secret"] = "integration-test-secret-at-least-32-chars",
            ["Authentication__Google__ClientId"] = "test-client-id",
            ["Authentication__Google__ClientSecret"] = "test-client-secret",
            ["Authentication__AllowedEmails"] = "admin@example.com",
            ["ConnectionStrings__Gateway"] = $"Data Source={_dbPath}",
        };
        try
        {
            foreach (var (key, value) in envVars)
                Environment.SetEnvironmentVariable(key, value);

            var factory = CreateIsolatedFactory("Production", new()
            {
                ["Authentication:DevBypass"] = "false",
            });
            var client = factory.CreateClient();
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            foreach (var key in envVars.Keys)
                Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public async Task SqliteConnectionString_IsConfigurable_ForVolumePersistence()
    {
        var factory = CreateIsolatedFactory("Development", new()
        {
            ["Authentication:DevBypass"] = "true",
            ["ConnectionStrings:Gateway"] = $"Data Source={_dbPath}",
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // EnsureCreated must have created the database at the configured path.
        Assert.True(File.Exists(_dbPath),
            $"Expected SQLite db at configured path {_dbPath}; " +
            "Program.cs is not honoring ConnectionStrings:Gateway.");
    }

    [Fact]
    public void DockerCompose_InjectsAuthConfig_AndPersistsSqlite()
    {
        var composePath = FindRepoFile("docker-compose.yml");
        var yaml = File.ReadAllText(composePath);

        // Keys the app actually binds (Authentication:Jwt:*, Google, AllowedEmails).
        Assert.Contains("Authentication__Jwt__Secret", yaml);
        Assert.Contains("Authentication__Jwt__Issuer", yaml);
        Assert.Contains("Authentication__Jwt__Audience", yaml);
        Assert.Contains("Authentication__Google__ClientId", yaml);
        Assert.Contains("Authentication__Google__ClientSecret", yaml);
        Assert.Contains("Authentication__AllowedEmails", yaml);

        // The old top-level Jwt__* keys are dead config the app never reads.
        Assert.DoesNotMatch(@"(?m)^\s*-\s*Jwt__", yaml);

        // Secrets come from the host environment without baked-in defaults.
        Assert.DoesNotContain("CHANGE_ME", yaml);
        Assert.DoesNotContain("GOCSPX", yaml);

        // SQLite must live on a persistent volume at the path the app uses.
        Assert.Contains("ConnectionStrings__Gateway", yaml);
        Assert.Contains("/app/data", yaml);
    }

    private static string FindRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"{fileName} not found walking up from {AppContext.BaseDirectory}");
    }
}
