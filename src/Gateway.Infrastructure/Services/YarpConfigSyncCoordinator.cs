namespace Gateway.Infrastructure.Services;

/// <summary>
/// Singleton lock shared by every scoped <see cref="YarpConfigSyncService"/>
/// instance. The service is Scoped (one instance per request), so a lock held
/// as an instance field would not serialize syncs across DI scopes; this
/// coordinator makes the repository-read-through-config/snapshot-update
/// critical section exclusive process-wide.
/// </summary>
public sealed class YarpConfigSyncCoordinator
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task WaitAsync(CancellationToken ct = default) => _lock.WaitAsync(ct);

    public void Release() => _lock.Release();
}
