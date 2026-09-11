using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Infrastructure.Services;

public sealed class ProxyEventRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProxyEventRetentionService> _logger;
    public ProxyEventRetentionService(IServiceScopeFactory scopeFactory, ILogger<ProxyEventRetentionService> logger) { _scopeFactory = scopeFactory; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            try
            {
                while (await DeleteExpiredBatchAsync(stoppingToken) == 10_000) { }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { _logger.LogWarning(ex, "Proxy telemetry retention failed"); }
        } while (await WaitOneDay(stoppingToken));
    }

    internal async Task<int> DeleteExpiredBatchAsync(CancellationToken ct, DateTime? cutoff = null)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        return await db.ProxyRequestEvents.Where(x => x.OccurredAt < (cutoff ?? DateTime.UtcNow.AddDays(-30))).OrderBy(x => x.OccurredAt).Take(10_000).ExecuteDeleteAsync(ct);
    }
    private static async Task<bool> WaitOneDay(CancellationToken ct) { try { await Task.Delay(TimeSpan.FromDays(1), ct); return true; } catch (OperationCanceledException) { return false; } }
}
