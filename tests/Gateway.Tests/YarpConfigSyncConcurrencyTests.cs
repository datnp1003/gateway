using Gateway.Domain.Entities;
using Gateway.Domain.Interfaces;
using Gateway.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Tests;

/// <summary>
/// Concurrent SyncFromDatabaseAsync calls must be serialized ACROSS DI SCOPES:
/// the service is registered Scoped, so each HTTP request gets its own
/// instance. A sync whose repository read started earlier must not overwrite
/// the YARP config and policy snapshot produced by a later sync running on a
/// DIFFERENT service instance (stale-read overwrite race). The test therefore
/// constructs two service instances that share only the singleton-lifetime
/// dependencies, exactly as production DI does.
/// </summary>
public class YarpConfigSyncConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSyncs_AcrossScopedInstances_StaleReadDoesNotOverwriteNewerConfig()
    {
        var group = new ProxyGroup { Id = Guid.NewGuid(), Name = "g", Path = "g" };
        var endpointA = MakeEndpoint(group, "/a/{**catch-all}");
        var endpointB = MakeEndpoint(group, "/b/{**catch-all}");

        var repo = new SequencedRepository(
            firstResult: new List<ProxyEndpoint> { endpointA },
            secondResult: new List<ProxyEndpoint> { endpointA, endpointB });

        // Singletons in production DI: shared across scopes.
        var configProvider = new InMemoryConfigProvider(new List<RouteConfig>(), new List<ClusterConfig>());
        var snapshot = new EndpointPolicySnapshot();
        var coordinator = new YarpConfigSyncCoordinator();

        // Scoped in production DI: one instance per request/scope.
        var serviceScope1 = new YarpConfigSyncService(repo, configProvider, snapshot, coordinator,
            NullLogger<YarpConfigSyncService>.Instance);
        var serviceScope2 = new YarpConfigSyncService(repo, configProvider, snapshot, coordinator,
            NullLogger<YarpConfigSyncService>.Instance);

        // Sync #1 starts first and its repository read stalls (stale data).
        var sync1 = serviceScope1.SyncFromDatabaseAsync();
        await repo.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Sync #2 starts on a different scope's instance while #1 is still
        // in flight, and sees fresher data.
        var sync2 = serviceScope2.SyncFromDatabaseAsync();

        // Give #2 a chance to finish; when syncs are serialized it stays
        // blocked behind #1, which is the correct behavior.
        await Task.WhenAny(sync2, Task.Delay(TimeSpan.FromSeconds(1)));

        // Now the stale read completes.
        repo.ReleaseFirstCall.TrySetResult();
        await Task.WhenAll(sync1, sync2).WaitAsync(TimeSpan.FromSeconds(5));

        // The fresher result (2 endpoints) must win, regardless of completion order.
        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Equal(2, configProvider.GetConfig().Routes.Count);
        Assert.Equal(2, configProvider.GetConfig().Clusters.Count);
    }

    private static ProxyEndpoint MakeEndpoint(ProxyGroup group, string pathPattern) => new()
    {
        Id = Guid.NewGuid(),
        GroupId = group.Id,
        Group = group,
        Name = "ep",
        PathPattern = pathPattern,
        Destination = "http://localhost:59999/"
    };

    /// <summary>
    /// First GetAllEnabledEndpointsAsync call signals it has started and then
    /// blocks until released; the second returns immediately with fresher data.
    /// </summary>
    private sealed class SequencedRepository : IProxyConfigRepository
    {
        private readonly List<ProxyEndpoint> _firstResult;
        private readonly List<ProxyEndpoint> _secondResult;
        private int _calls;

        public TaskCompletionSource FirstCallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SequencedRepository(List<ProxyEndpoint> firstResult, List<ProxyEndpoint> secondResult)
        {
            _firstResult = firstResult;
            _secondResult = secondResult;
        }

        public async Task<List<ProxyEndpoint>> GetAllEnabledEndpointsAsync(CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstCallStarted.TrySetResult();
                await ReleaseFirstCall.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                return _firstResult;
            }
            return _secondResult;
        }

        public Task<List<ProxyGroup>> GetAllGroupsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProxyGroup?> GetGroupByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProxyGroup> CreateGroupAsync(ProxyGroup group, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProxyGroup> UpdateGroupAsync(ProxyGroup group, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteGroupAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<ProxyEndpoint>> GetEndpointsByGroupAsync(Guid groupId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProxyEndpoint> CreateEndpointAsync(ProxyEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ProxyEndpoint> UpdateEndpointAsync(ProxyEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteEndpointAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
