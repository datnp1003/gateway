using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Gateway.Domain.Interfaces;

namespace Gateway.Infrastructure.Services;

public class YarpConfigSyncService : IYarpConfigSyncService
{
    private readonly IProxyConfigRepository _repo;
    private readonly InMemoryConfigProvider _configProvider;
    private readonly ILogger<YarpConfigSyncService> _logger;

    public YarpConfigSyncService(IProxyConfigRepository repo, InMemoryConfigProvider configProvider,
        ILogger<YarpConfigSyncService> logger)
    {
        _repo = repo;
        _configProvider = configProvider;
        _logger = logger;
    }

    public async Task SyncFromDatabaseAsync(CancellationToken ct = default)
    {
        var endpoints = await _repo.GetAllEnabledEndpointsAsync(ct);

        var routes = endpoints.Select(e =>
        {
            var groupPath = e.Group.Path.TrimStart('/');
            var fullPath = $"/{groupPath}{e.PathPattern}";
            var removePrefix = "/" + groupPath;

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
                Metadata = new Dictionary<string, string>
                {
                    { "RateLimiterPolicy", "proxy" }
                }
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

        _logger.LogInformation("YARP sync: {RouteCount} routes, {ClusterCount} clusters", routes.Count, clusters.Count);
        foreach (var e in endpoints)
        {
            _logger.LogInformation(
                "YARP route mapping: {RouteId} [{Path}] -> {ClusterId} -> {Destination}",
                $"route-{e.Id}", $"/{e.Group.Path.TrimStart('/')}{e.PathPattern}", $"cluster-{e.Id}", e.Destination);
        }
    }
}
