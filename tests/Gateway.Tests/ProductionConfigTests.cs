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
/// must persist configuration in PostgreSQL using the supplied connection string.
/// </summary>
public class ProductionConfigTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly TestDatabase _database = new();

    public ProductionConfigTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _database.Dispose();
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
            ["ConnectionStrings:Gateway"] = _database.ConnectionString,
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
            ["ConnectionStrings__Gateway"] = _database.ConnectionString,
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
    public async Task PostgresConnectionString_IsConfigurable_AndMigrationsAreRepeatable()
    {
        var factory = CreateIsolatedFactory("Development", new()
        {
            ["Authentication:DevBypass"] = "true",
            ["ConnectionStrings:Gateway"] = _database.ConnectionString,
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(db.Database.HasPendingModelChanges());
        await db.Database.MigrateAsync();
        Assert.Single(await db.Groups.ToListAsync());
    }

    [Fact]
    public void DockerCompose_InjectsAuthConfig_AndPersistsPostgres()
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

        // PostgreSQL data must live on its own persistent volume.
        Assert.Contains("ConnectionStrings__Gateway", yaml);
        Assert.Contains("/var/lib/postgresql/data", yaml);
        Assert.Contains("service_healthy", yaml);
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
