namespace Gateway.Domain.Interfaces;

public interface IYarpConfigSyncService
{
    Task SyncFromDatabaseAsync(CancellationToken ct = default);
}
