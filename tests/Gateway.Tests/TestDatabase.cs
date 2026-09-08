using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Npgsql;

namespace Gateway.Tests;

// Only creates/drops a randomly named database; never resets the supplied database.
internal sealed class TestDatabase : IDisposable
{
    private readonly string _admin;
    private readonly string _name = "gateway_test_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; }

    public TestDatabase()
    {
        _admin = Environment.GetEnvironmentVariable("GATEWAY_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Run scripts/test-postgres.sh or set GATEWAY_TEST_POSTGRES to an isolated PostgreSQL server with CREATEDB permission.");
        var settings = new NpgsqlConnectionStringBuilder(_admin) { Database = _name, Pooling = false };
        ConnectionString = settings.ConnectionString;
        using var connection = new NpgsqlConnection(_admin);
        connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{_name}\"", connection);
        command.ExecuteNonQuery();
    }

    public void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Gateway", ConnectionString);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            foreach (var source in config.Sources.OfType<JsonConfigurationSource>().ToList())
                config.Sources.Remove(source);
            var seed = new Dictionary<string, string?>();
            foreach (var (name, port) in new[] { ("auth", 5100), ("learning", 5101), ("ai", 5102) })
            {
                seed[$"ReverseProxy:Routes:{name}:ClusterId"] = name;
                seed[$"ReverseProxy:Routes:{name}:Match:Path"] = $"/api/{name}/{{**catch-all}}";
                seed[$"ReverseProxy:Clusters:{name}:Destinations:default:Address"] = $"http://localhost:{port}/";
            }
            config.AddInMemoryCollection(seed);
        });
    }

    public void Dispose()
    {
        using var connection = new NpgsqlConnection(_admin);
        connection.Open();
        using var command = new NpgsqlCommand($"DROP DATABASE \"{_name}\" WITH (FORCE)", connection);
        command.ExecuteNonQuery();
    }
}
