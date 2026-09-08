using Gateway.Domain.Entities;
using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
}
