using System.Text.Json;
using Gateway.Domain.Entities;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gateway.Tests;

public class DashboardReadTests
{
    [Fact]
    public async Task OperationsSummary_UsesSqlAggregatesForOutcomesBucketsRankingsAndPreviousWindow()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var endpoint = Guid.NewGuid();
        var otherEndpoint = Guid.NewGuid();
        var group = Guid.NewGuid();
        var start = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        db.ProxyRequestEvents.AddRange(
            Event(start.AddMinutes(-1), endpoint, group, "old", "upstream_response", 200, 20),
            Event(start.AddSeconds(10), endpoint, group, "old", "upstream_response", 200, 10),
            Event(start.AddSeconds(20), endpoint, group, "old", "upstream_response", 503, 20),
            Event(start.AddSeconds(30), endpoint, group, "latest", "gateway_rejected", 429, 30),
            Event(start.AddSeconds(40), endpoint, group, "latest", "network_failure", null, 40),
            Event(start.AddSeconds(50), endpoint, group, "latest", "client_disconnected", null, null),
            Event(start.AddMinutes(1).AddSeconds(10), otherEndpoint, group, "other", "upstream_response", 404, 100));
        await db.SaveChangesAsync();

        var summary = await new ProxyOperationsQueryService(db).GetSummaryAsync(start, start.AddMinutes(2), TimeSpan.FromMinutes(1), group, null, CancellationToken.None);

        Assert.Equal(6, summary.Current.Attempts);
        Assert.Equal(4, summary.Current.FailedRequests);
        Assert.Equal(1, summary.Current.GatewayRejected);
        Assert.Equal(1, summary.Current.NetworkFailures);
        Assert.Equal(1, summary.Current.ClientDisconnected);
        Assert.Equal(1, summary.Current.Upstream5xx);
        Assert.Equal(40, summary.Current.AverageLatencyMs);
        Assert.Equal(88, summary.Current.P95LatencyMs!.Value, 8);
        Assert.Equal(1, summary.Previous!.Attempts);
        Assert.Equal(2, summary.Series.Count);
        Assert.Equal(5, summary.Series[0].Attempts);
        Assert.Equal(3, summary.Series[0].FailedRequests);
        Assert.Equal(25, summary.Series[0].AverageLatencyMs);
        Assert.Equal(38.5, summary.Series[0].P95LatencyMs!.Value, 8);
        Assert.Equal(1, summary.Series[1].Attempts);
        var top = Assert.Single(summary.Rankings, item => item.EndpointId == endpoint);
        Assert.Equal("latest", top.EndpointName);
        Assert.Equal(5, top.Attempts);
        Assert.Equal(3, top.FailedRequests);
        Assert.Equal(38.5, top.P95LatencyMs!.Value, 8);
    }

    [Fact]
    public async Task Overview_UsesIndependentUtcWindowsAndObservedTrafficOnly()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var group = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var now = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        db.Groups.Add(new ProxyGroup
        {
            Id = group, Name = "group", Path = "group",
            Endpoints = [new ProxyEndpoint { Id = endpoint, Name = "orders", PathPattern = "/{**path}", Destination = "https://backend.example" }]
        });
        db.ProxyRequestEvents.AddRange(
            Event(now.AddSeconds(-20), endpoint, group, "orders", "upstream_response", 200, 100),
            Event(now.AddSeconds(-10), endpoint, group, "orders", "upstream_response", 503, 300),
            Event(now.AddMinutes(-3), endpoint, group, "orders", "network_failure", null, 50),
            Event(now.AddHours(-2), endpoint, group, "orders", "upstream_response", 404, 75),
            Event(now.Date.AddMinutes(5), endpoint, group, "orders", "upstream_response", 200, 25),
            Event(now.Date.AddDays(-1).AddMinutes(5), endpoint, group, "orders", "upstream_response", 200, 25));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(now, CancellationToken.None);

        Assert.Equal(2, overview.CurrentMinute.Attempts);
        Assert.Equal(5, overview.Today.Attempts);
        Assert.Equal(1, overview.Yesterday.Attempts);
        // Events have no ClientIp → unique clients is 0.
        Assert.Equal(0, overview.Today.UniqueClients);
        Assert.Equal(0, overview.Yesterday.UniqueClients);
        Assert.Equal(1, Assert.Single(overview.StatusErrors).Attempts);
        Assert.Equal(503, Assert.Single(overview.StatusErrors).Status);
        Assert.Equal(1, overview.LastHour.NetworkFailures);
        var recent = Assert.Single(overview.RecentEndpoints);
        Assert.Equal("GET", recent.Method);
        Assert.Equal("/safe/{**path}", recent.RequestPath);
        Assert.Equal(503, recent.LatestResponseStatus);
        var observedGroup = Assert.Single(overview.Groups);
        Assert.Equal(3, observedGroup.Attempts);
        Assert.Equal(1, observedGroup.ObservedEndpoints);
        Assert.Equal(1, observedGroup.SuccessfulResponses);

        // Per-group 1h series: one entry per displayed group, 60 deterministic ascending minute buckets over now-1h→now.
        var groupSeries = Assert.Single(overview.GroupsHourly);
        Assert.Equal(observedGroup.GroupId, groupSeries.Key);
        Assert.Equal(60, groupSeries.Value.Count);
        Assert.True(groupSeries.Value.Zip(groupSeries.Value.Skip(1)).All(pair => pair.First.From < pair.Second.From));
        Assert.Equal(TimeSpan.FromMinutes(1), groupSeries.Value[0].To - groupSeries.Value[0].From);
        Assert.Equal(now.AddHours(-1), groupSeries.Value[0].From);
        // Real observed attempts land in their own minute bucket; empty minutes are zero-filled, not fabricated.
        // now=12:30:00 → the two -20s/-10s attempts share bucket [12:29,12:30); the -3min attempt sits in [12:27,12:28).
        Assert.Equal(3, groupSeries.Value.Sum(bucket => bucket.Attempts));
        Assert.Equal(2, groupSeries.Value.Single(bucket => bucket.From == now.AddMinutes(-1)).Attempts);
        Assert.Equal(1, groupSeries.Value.Single(bucket => bucket.From == now.AddMinutes(-3)).Attempts);
        Assert.Equal(58, groupSeries.Value.Count(bucket => bucket.Attempts == 0));
    }

    [Fact]
    public async Task Overview_ShowsConfiguredTopFiveGroupsWhenThereIsNoRecentTraffic()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        db.Groups.AddRange(Enumerable.Range(0, 6).Select(i => new ProxyGroup
        {
            Id = Guid.NewGuid(), Name = $"group-{i}", Path = $"group-{i}",
            Endpoints = [new ProxyEndpoint { Name = $"endpoint-{i}", PathPattern = "/{**path}", Destination = "https://backend.example" }]
        }));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.Equal(5, overview.Groups.Count);
        Assert.All(overview.Groups, group =>
        {
            Assert.Equal(0, group.Attempts);
            Assert.Equal(1, group.ObservedEndpoints);
            Assert.Equal(0, group.SuccessfulResponses);
            Assert.Equal(60, overview.GroupsHourly[group.GroupId].Count);
            Assert.All(overview.GroupsHourly[group.GroupId], bucket => Assert.Equal(0, bucket.Attempts));
        });
    }

    private static ProxyRequestEvent Event(DateTime occurredAt, Guid endpointId, Guid groupId, string endpointName, string outcome, int? status, double? duration) => new()
    {
        Id = Guid.NewGuid(), OccurredAt = occurredAt, CompletedAt = occurredAt, Method = "GET", RequestPath = "/safe/{**path}", EndpointId = endpointId, GroupId = groupId,
        EndpointName = endpointName, GroupName = "group", ConfiguredDestination = "https://backend.example", Outcome = outcome, ResponseStatus = status, DurationMs = duration
    };

    private static ProxyRequestEvent EventWithIp(DateTime occurredAt, Guid endpointId, Guid groupId, string endpointName, string outcome, int? status, double? duration, string? clientIp) => new()
    {
        Id = Guid.NewGuid(), OccurredAt = occurredAt, CompletedAt = occurredAt, Method = "GET", RequestPath = "/safe/{**path}", EndpointId = endpointId, GroupId = groupId,
        EndpointName = endpointName, GroupName = "group", ConfiguredDestination = "https://backend.example", Outcome = outcome, ResponseStatus = status, DurationMs = duration, ClientIp = clientIp
    };

    [Fact]
    public async Task Overview_UniqueIpsDeduplicatesWithinHourAndAcrossHours()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var group = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var now = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        // Same IP "10.0.0.1" makes 3 requests in the same hour → unique count = 1.
        // IP "10.0.0.2" makes 1 request in a different hour → unique count = 2 for today.
        db.ProxyRequestEvents.AddRange(
            EventWithIp(now.AddSeconds(-20), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.AddSeconds(-10), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.AddMinutes(-3), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.AddHours(-2), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.2"));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(now, CancellationToken.None);

        // Today: 2 distinct IPs across 4 requests.
        Assert.Equal(2, overview.Today.UniqueClients);
        // Per-bucket: the 12:00-12:30 bucket has 3 requests from same IP → uniqueClients=1.
        var bucket1200 = overview.Traffic.Single(b => b.From == now.Date.AddHours(12));
        Assert.Equal(1, bucket1200.UniqueClients);
        // The 10:00-11:00 bucket has 1 request from a different IP → uniqueClients=1.
        var bucket1000 = overview.Traffic.Single(b => b.From == now.Date.AddHours(10));
        Assert.Equal(1, bucket1000.UniqueClients);
        // Daily unique (2) == sum of hourly uniques (1+1=2) in this case.
        Assert.Equal(overview.Today.UniqueClients, overview.Traffic.Sum(b => b.UniqueClients));
    }

    [Fact]
    public async Task Overview_UniqueIpsExcludesNullAndEmpty()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var group = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var now = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        // Events with null and empty ClientIp are excluded from unique count.
        db.ProxyRequestEvents.AddRange(
            EventWithIp(now.AddSeconds(-20), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.AddSeconds(-10), endpoint, group, "orders", "upstream_response", 200, 10, null),
            EventWithIp(now.AddSeconds(-5), endpoint, group, "orders", "upstream_response", 200, 10, ""),
            EventWithIp(now.AddSeconds(-3), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.2"));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(now, CancellationToken.None);

        // Only 2 distinct non-null non-empty IPs.
        Assert.Equal(2, overview.Today.UniqueClients);
        Assert.Equal(4, overview.Today.Attempts);
    }

    [Fact]
    public async Task Overview_UniqueIpsDistinguishesIPv4AndIPv6()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var group = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var now = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        // "::ffff:10.0.0.1" and "10.0.0.1" are stored as distinct strings → counted separately.
        // This is by design: the middleware captures RemoteIpAddress.ToString() as-is; no unsafe normalization.
        db.ProxyRequestEvents.AddRange(
            EventWithIp(now.AddSeconds(-20), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.AddSeconds(-10), endpoint, group, "orders", "upstream_response", 200, 10, "::ffff:10.0.0.1"),
            EventWithIp(now.AddSeconds(-5), endpoint, group, "orders", "upstream_response", 200, 10, "2001:db8::1"));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(now, CancellationToken.None);

        // Three distinct string values.
        Assert.Equal(3, overview.Today.UniqueClients);
    }

    [Fact]
    public async Task Overview_UniqueIpsYesterdayUsesCorrectLocalMidnight()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var group = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        // Now is 2026-09-08 12:30 UTC. Today midnight = 2026-09-08 00:00 UTC. Yesterday = 2026-09-07.
        var now = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        db.ProxyRequestEvents.AddRange(
            // Today: 2 distinct IPs.
            EventWithIp(now.AddHours(-1), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.AddHours(-2), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.2"),
            // Yesterday: 3 distinct IPs.
            EventWithIp(now.Date.AddDays(-1).AddHours(1), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.Date.AddDays(-1).AddHours(2), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.3"),
            EventWithIp(now.Date.AddDays(-1).AddHours(3), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.4"),
            // Before yesterday: should not count.
            EventWithIp(now.Date.AddDays(-2).AddHours(1), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.5"));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(now, CancellationToken.None);

        Assert.Equal(2, overview.Today.UniqueClients);
        Assert.Equal(3, overview.Yesterday.UniqueClients);
    }

    [Fact]
    public async Task Overview_UniqueIpsDailyNotEqualToSumHourlyWhenSameIpSpansMultipleHours()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var group = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var now = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        // IP "10.0.0.1" appears in 3 different hourly buckets.
        // Daily unique = 1. Sum of hourly uniques = 3.
        db.ProxyRequestEvents.AddRange(
            EventWithIp(now.Date.AddHours(2), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.Date.AddHours(6), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"),
            EventWithIp(now.Date.AddHours(10), endpoint, group, "orders", "upstream_response", 200, 10, "10.0.0.1"));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(now, CancellationToken.None);

        Assert.Equal(1, overview.Today.UniqueClients);
        Assert.Equal(3, overview.Traffic.Sum(b => b.UniqueClients));
        Assert.NotEqual(overview.Today.UniqueClients, overview.Traffic.Sum(b => b.UniqueClients));
    }

    [Fact]
    public async Task Overview_ExistingSummaryRegression_UniqueClientsDefaultsToZero()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options;
        var group = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var now = new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);
        await using var db = new GatewayDbContext(options);
        await db.Database.MigrateAsync();
        // Events with no ClientIp set (legacy/pre-migration data).
        db.ProxyRequestEvents.AddRange(
            Event(now.AddSeconds(-20), endpoint, group, "orders", "upstream_response", 200, 100),
            Event(now.AddSeconds(-10), endpoint, group, "orders", "upstream_response", 503, 300));
        await db.SaveChangesAsync();

        var overview = await new ProxyOperationsQueryService(db).GetOverviewAsync(now, CancellationToken.None);

        // Existing behavior preserved: attempts, error counts, etc.
        Assert.Equal(2, overview.Today.Attempts);
        Assert.Equal(1, overview.Today.FailedRequests);
        // New field: zero when all ClientIp are null.
        Assert.Equal(0, overview.Today.UniqueClients);
        // Traffic series also has zero uniqueClients per bucket.
        Assert.All(overview.Traffic, bucket => Assert.Equal(0, bucket.UniqueClients));
    }

    [Fact]
    public void PagesAllRetainedFilesAndFiltersBeforePaging()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllLines(Path.Combine(directory, "gateway-20260101.log"), Enumerable.Range(0, 650)
                .Select(i => $"2026-01-01 00:00:00.000 +00:00 [INF] [ProxyRequest] [GET] /old/{i} -> 200 (1.5ms)"));
            File.WriteAllText(Path.Combine(directory, "gateway-20260102.log"), "2026-01-02 00:00:00.000 +00:00 [ERR] failure\nstack line\n2026-01-02 00:00:01.000 +00:00 [INF] [ProxyRequest] [POST] /new -> 500 (2.5ms)\n");
            File.AppendAllLines(Path.Combine(directory, "gateway-20260102.log"), new[] {
                "2026-01-02 00:00:02.000 +00:00 [INF] Executed DbCommand (1ms)",
                "2026-01-02 00:00:02.000 +00:00 [INF] HTTP GET /api/management/logs/page responded 200",
                "2026-01-02 00:00:02.000 +00:00 [INF] [GET] /legacy -> 200 (1ms)",
                "2026-01-02 00:00:02.000 +00:00 [INF] [ProxyRequest] [GET] /API/Auth/me -> 200 (1ms)"
            });
            JsonElement Query(int page = 1, string? level = null, string? search = null, int? status = null, DateTimeOffset? before = null) =>
                JsonSerializer.SerializeToElement(RetainedLogs.Query(directory, page, 25, level, search, status, before));
            var first = Query();
            Assert.Equal(651, first.GetProperty("total").GetInt32());
            Assert.Equal(27, first.GetProperty("totalPages").GetInt32());
            Assert.Equal("/new", first.GetProperty("items")[0].GetProperty("Path").GetString());
            Assert.DoesNotContain("failure", first.ToString());
            var last = Query(27);
            Assert.Equal(1, last.GetProperty("items").GetArrayLength());
            Assert.Equal("/old/0", last.GetProperty("items")[0].GetProperty("Path").GetString());
            Assert.False(last.GetProperty("hasNext").GetBoolean());
            Assert.Equal(1, Query(search: "/OLD/0").GetProperty("total").GetInt32());
            Assert.Equal(1, Query(status: 500).GetProperty("total").GetInt32());
            Assert.Equal(0, Query(level: "Error").GetProperty("total").GetInt32());
            Assert.Equal(650, Query(before: DateTimeOffset.Parse("2026-01-01T23:59:59Z")).GetProperty("total").GetInt32());
            Assert.Empty(Query(100).GetProperty("items").EnumerateArray());
            Assert.Throws<ArgumentException>(() => RetainedLogs.Query(directory, 0, 25, null, null, null, null));
            Assert.Throws<ArgumentException>(() => RetainedLogs.Query(directory, 1, 101, null, null, null, null));
            Assert.Throws<ArgumentException>(() => RetainedLogs.Query(directory, 1, 25, "invalid", null, null, null));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MetricsUseCompletedResponsesAndSixtySecondRate()
    {
        var metrics = new MetricsTracker();
        metrics.TrackRequest("/test", 200, 10);
        metrics.TrackRequest("/test", 500, 30);
        var result = metrics.GetMetrics();
        Assert.Equal(2, result.TotalRequests);
        Assert.Equal(2 / 60.0, result.RequestsPerSecond);
        Assert.Equal(50, result.ErrorRate);
        Assert.Equal(20, result.AvgLatencyMs);
        Assert.Equal(30, result.P95LatencyMs);
    }
}
