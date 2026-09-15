using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Services;
using Gateway.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Gateway.Tests;
public class LocalCalendarTests
{
    [Theory]
    [InlineData("Asia/Bangkok", "2026-09-14T17:00:01Z", "2026-09-14T17:00:00Z", 24)]
    [InlineData("America/New_York", "2026-03-09T04:00:01Z", "2026-03-09T04:00:00Z", 23)]
    [InlineData("America/New_York", "2026-11-02T05:00:01Z", "2026-11-02T05:00:00Z", 25)]
    public async Task LocalDays_QueryHalfOpenCalendarWindows(string id, string instant, string expected, int yesterdayHours)
    {
        var now = DateTime.Parse(instant).ToUniversalTime();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        var day = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;
        var start = TimeZoneInfo.ConvertTimeToUtc(day, zone);
        var prior = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(-1), zone);
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), start);
        Assert.Equal(yesterdayHours, (start-prior).TotalHours);
        using var database = new TestDatabase();
        await using var db = new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql(database.ConnectionString).Options);
        await db.Database.MigrateAsync();
        foreach (var at in new[] { prior.AddSeconds(-1), prior, start.AddSeconds(-1), start, now })
            db.ProxyRequestEvents.Add(new ProxyRequestEvent { OccurredAt = at, EndpointId = Guid.NewGuid(), GroupId = Guid.NewGuid(), EndpointName = "calendar", GroupName = "calendar", Method = "GET", RequestPath = "/calendar", Outcome = "upstream_response", ResponseStatus = 200 });
        await db.SaveChangesAsync();
        var service = new ProxyOperationsQueryService(db);
        var result = await service.GetOverviewAsync(now, default, start, prior);
        Assert.Equal(1, result.Today.Attempts);
        Assert.Equal(2, result.Yesterday.Attempts);
        Assert.Equal(1, result.Traffic.Sum(x => x.Attempts));
        var later = await service.GetOverviewAsync(now.AddHours(1), default, start, prior);
        Assert.Equal(2, later.Yesterday.Attempts);
    }
}
