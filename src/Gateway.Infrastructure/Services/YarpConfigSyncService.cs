using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Gateway.Domain.Interfaces;

namespace Gateway.Infrastructure.Services;

public class YarpConfigSyncService : IYarpConfigSyncService
{
    private readonly IProxyConfigRepository _repo;
    private readonly InMemoryConfigProvider _configProvider;
    private readonly EndpointPolicySnapshot _policySnapshot;
    private readonly ILogger<YarpConfigSyncService> _logger;

    // Serializes concurrent syncs so a sync that read the database earlier
    // cannot overwrite the config/snapshot written by a later sync. Must be
    // the singleton coordinator: this service is Scoped, so an instance
    // field would not be shared across requests.
    private readonly YarpConfigSyncCoordinator _syncLock;

    public YarpConfigSyncService(IProxyConfigRepository repo, InMemoryConfigProvider configProvider,
        EndpointPolicySnapshot policySnapshot, YarpConfigSyncCoordinator syncCoordinator,
        ILogger<YarpConfigSyncService> logger)
    {
        _repo = repo;
        _configProvider = configProvider;
        _policySnapshot = policySnapshot;
        _syncLock = syncCoordinator;
        _logger = logger;
    }

    public async Task SyncFromDatabaseAsync(CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            var endpoints = await _repo.GetAllEnabledEndpointsAsync(ct);

            var routes = endpoints.Select(e =>
            {
                var groupPath = e.Group.Path.TrimStart('/');
                var fullPath = $"/{groupPath}{e.PathPattern}";
                // Endpoint-level RemovePrefix wins; default strips only the group path.
                var removePrefix = !string.IsNullOrWhiteSpace(e.RemovePrefix)
                    ? e.RemovePrefix
                    : "/" + groupPath;

                var transforms = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string>
                    {
                        { "PathRemovePrefix", removePrefix }
                    }
                };

                // Auth pass-through: downstream services own auth, so no AuthorizationPolicy here.
                return new RouteConfig
                {
                    RouteId = $"route-{e.Id}",
                    ClusterId = $"cluster-{e.Id}",
                    Match = new RouteMatch { Path = fullPath },
                    Transforms = transforms,
                    // Global per-IP proxy limit; per-endpoint limits are enforced
                    // separately in EndpointAccessPolicyMiddleware.
                    RateLimiterPolicy = "proxy"
                };
            }).ToList();

            // One cluster per endpoint: each endpoint proxies only to its own destination,
            // never load-balanced across other endpoints in the same group.
            var clusters = endpoints
                .Select(e => new ClusterConfig
                {
                    ClusterId = $"cluster-{e.Id}",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        { $"dest-{e.Id}", new DestinationConfig { Address = e.Destination } }
                    }
                }).ToList();

            _configProvider.Update(routes, clusters);
            _policySnapshot.Update(endpoints);

            _logger.LogInformation("YARP sync: {RouteCount} routes, {ClusterCount} clusters", routes.Count, clusters.Count);
            foreach (var e in endpoints)
            {
                _logger.LogInformation(
                    "YARP route mapping: {RouteId} [{Path}] -> {ClusterId} -> {Destination}",
                    $"route-{e.Id}", $"/{e.Group.Path.TrimStart('/')}{e.PathPattern}", $"cluster-{e.Id}", e.Destination);
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }
}
