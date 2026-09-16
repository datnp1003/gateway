using System.Net;
using Gateway.Domain.Entities;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

public class PostgresPersistenceTests
{
    [Fact]
    public async Task RestartPreservesPolicies_AndDatabaseEnforcesConstraints()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var id = Guid.NewGuid();
        await using (var db = new GatewayDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Groups.Add(new ProxyGroup { Id = id, Name = "persistent", Path = "persistent", AllowedIpRanges = "10.0.0.0/8",
                Endpoints = [new ProxyEndpoint { Name = "endpoint", PathPattern = "/{**rest}", Destination = "http://localhost:5100", RateLimitPerMinute = 7 }] });
            await db.SaveChangesAsync();
        }
        await using (var db = new GatewayDbContext(options))
        {
            await db.Database.MigrateAsync();
            var group = await db.Groups.Include(g => g.Endpoints).SingleAsync();
            Assert.Equal(id, group.Id);
            Assert.Equal("10.0.0.0/8", group.AllowedIpRanges);
            Assert.Equal(7, Assert.Single(group.Endpoints).RateLimitPerMinute);
            Assert.Equal(DateTimeKind.Utc, group.CreatedAt.Kind);
            db.Groups.Add(new ProxyGroup { Name = group.Name, Path = "duplicate" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.Groups.Remove(await db.Groups.SingleAsync());
            await db.SaveChangesAsync();
            Assert.Empty(await db.Endpoints.ToListAsync());
            db.Endpoints.Add(new ProxyEndpoint { GroupId = id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ConcurrentSeed_IsAtomicAndIdempotent()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var first = new GatewayDbContext(options);
        await using var second = new GatewayDbContext(options);
        await first.Database.MigrateAsync();
        var config = new ConfigurationBuilder().Build();
        await Task.WhenAll(SeedData.SeedFromAppSettingsAsync(first, config), SeedData.SeedFromAppSettingsAsync(second, config));
        Assert.Single(await first.Groups.ToListAsync());
    }

    [Fact]
    public async Task ProxyEvents_ArePersistent_AndRetainImmutableSnapshots()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var id = Guid.NewGuid();
        await using (var db = new GatewayDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.ProxyRequestEvents.Add(new ProxyRequestEvent { Id = id, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/catalog/{**rest}", GroupId = Guid.NewGuid(), EndpointId = Guid.NewGuid(), GroupName = "catalog", EndpointName = "items", ConfiguredDestination = "https://backend.example", Outcome = "upstream_response", ResponseStatus = 503 });
            await db.SaveChangesAsync();
        }
        await using (var db = new GatewayDbContext(options))
        {
            var item = await db.ProxyRequestEvents.SingleAsync();
            Assert.Equal(id, item.Id);
            Assert.Equal("/catalog/{**rest}", item.RequestPath);
            Assert.Equal(503, item.ResponseStatus);
            db.ProxyRequestEvents.Add(new ProxyRequestEvent { Id = Guid.NewGuid(), OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/safe", GroupId = Guid.NewGuid(), EndpointId = Guid.NewGuid(), GroupName = "g", EndpointName = "e", ConfiguredDestination = "https://backend.example", Outcome = "bad" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ClientIp_PersistsIPv4AndIPv6_WithNullForOldEvents()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var ipv4Id = Guid.NewGuid();
        var ipv6Id = Guid.NewGuid();
        var nullId = Guid.NewGuid();

        await using (var db = new GatewayDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.ProxyRequestEvents.AddRange(
                new ProxyRequestEvent { Id = ipv4Id, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/api/data", GroupId = Guid.NewGuid(), EndpointId = Guid.NewGuid(), GroupName = "g", EndpointName = "e", ConfiguredDestination = "http://backend", Outcome = "upstream_response", ResponseStatus = 200, ClientIp = "192.168.1.100" },
                new ProxyRequestEvent { Id = ipv6Id, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "POST", RequestPath = "/api/data", GroupId = Guid.NewGuid(), EndpointId = Guid.NewGuid(), GroupName = "g", EndpointName = "e", ConfiguredDestination = "http://backend", Outcome = "upstream_response", ResponseStatus = 201, ClientIp = "2001:db8::1" },
                new ProxyRequestEvent { Id = nullId, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/api/old", GroupId = Guid.NewGuid(), EndpointId = Guid.NewGuid(), GroupName = "g", EndpointName = "e", ConfiguredDestination = "http://backend", Outcome = "upstream_response", ResponseStatus = 200, ClientIp = null });
            await db.SaveChangesAsync();
        }

        await using (var db = new GatewayDbContext(options))
        {
            var ipv4 = await db.ProxyRequestEvents.FindAsync(ipv4Id);
            Assert.Equal("192.168.1.100", ipv4!.ClientIp);

            var ipv6 = await db.ProxyRequestEvents.FindAsync(ipv6Id);
            Assert.Equal("2001:db8::1", ipv6!.ClientIp);

            var old = await db.ProxyRequestEvents.FindAsync(nullId);
            Assert.Null(old!.ClientIp);
        }
    }

    [Fact]
    public async Task ClientIp_NullIsCorrectDefault_ForExistingRows()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var id = Guid.NewGuid();

        // Insert event without ClientIp (simulates pre-migration behavior)
        await using (var db = new GatewayDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.ProxyRequestEvents.Add(new ProxyRequestEvent { Id = id, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/legacy", GroupId = Guid.NewGuid(), EndpointId = Guid.NewGuid(), GroupName = "g", EndpointName = "e", ConfiguredDestination = "http://backend", Outcome = "upstream_response", ResponseStatus = 200 });
            await db.SaveChangesAsync();
        }

        await using (var db = new GatewayDbContext(options))
        {
            var item = await db.ProxyRequestEvents.FindAsync(id);
            Assert.NotNull(item);
            Assert.Null(item.ClientIp);
        }
    }

    [Fact]
    public async Task BulkWriter_PersistsClientIp_ViaRawSqlInsert()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var ipv4Id = Guid.NewGuid();
        var ipv6Id = Guid.NewGuid();
        var nullId = Guid.NewGuid();

        await using (var db = new GatewayDbContext(options))
            await db.Database.MigrateAsync();

        var committed = new TaskCompletionSource();
        var writer = new ProxyAttemptWriter(
            new StubScopeFactory(new GatewayDbContext(options)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProxyAttemptWriter>.Instance,
            capacity: 100,
            afterCommitForTesting: () => { committed.TrySetResult(); return Task.CompletedTask; });

        writer.TryEnqueue(new ProxyRequestEvent { Id = ipv4Id, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/a", EndpointId = Guid.NewGuid(), GroupId = Guid.NewGuid(), EndpointName = "e", GroupName = "g", ConfiguredDestination = "http://b", Outcome = "upstream_response", ResponseStatus = 200, ClientIp = "10.0.0.1" });
        writer.TryEnqueue(new ProxyRequestEvent { Id = ipv6Id, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/b", EndpointId = Guid.NewGuid(), GroupId = Guid.NewGuid(), EndpointName = "e", GroupName = "g", ConfiguredDestination = "http://b", Outcome = "upstream_response", ResponseStatus = 200, ClientIp = "::ffff:10.0.0.1" });
        writer.TryEnqueue(new ProxyRequestEvent { Id = nullId, OccurredAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, Method = "GET", RequestPath = "/c", EndpointId = Guid.NewGuid(), GroupId = Guid.NewGuid(), EndpointName = "e", GroupName = "g", ConfiguredDestination = "http://b", Outcome = "upstream_response", ResponseStatus = 200, ClientIp = null });

        await writer.StartAsync(CancellationToken.None);
        await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await writer.StopAsync(CancellationToken.None);

        await using (var db = new GatewayDbContext(options))
        {
            var ipv4 = await db.ProxyRequestEvents.FindAsync(ipv4Id);
            Assert.Equal("10.0.0.1", ipv4!.ClientIp);

            var ipv6 = await db.ProxyRequestEvents.FindAsync(ipv6Id);
            Assert.Equal("::ffff:10.0.0.1", ipv6!.ClientIp);

            var noIp = await db.ProxyRequestEvents.FindAsync(nullId);
            Assert.Null(noIp!.ClientIp);
        }
    }

    private sealed class StubScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly GatewayDbContext _db;
        public StubScopeFactory(GatewayDbContext db) => _db = db;
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) => serviceType == typeof(GatewayDbContext) ? _db : null;
        public void Dispose() { }
    }
}
