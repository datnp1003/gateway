using Yarp.ReverseProxy.Configuration;
using Gateway.Domain.Interfaces;

namespace Gateway.Infrastructure.Services;

public class YarpConfigSyncService : IYarpConfigSyncService
{
    private readonly IProxyConfigRepository _repo;
    private readonly InMemoryConfigProvider _configProvider;

    public YarpConfigSyncService(IProxyConfigRepository repo, InMemoryConfigProvider configProvider)
    {
        _repo = repo;
        _configProvider = configProvider;
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

            return new RouteConfig
            {
                RouteId = $"route-{e.Id}",
                ClusterId = $"cluster-{e.GroupId}",
                Match = new RouteMatch { Path = fullPath },
                AuthorizationPolicy = e.RequiresAuth ? "Authenticated" : null,
                Transforms = transforms,
                Metadata = new Dictionary<string, string>
                {
                    { "RateLimiterPolicy", "proxy" }
                }
            };
        }).ToList();

        var clusters = endpoints
            .GroupBy(e => e.GroupId)
            .Select(g => new ClusterConfig
            {
                ClusterId = $"cluster-{g.Key}",
                Destinations = g.ToDictionary(
                    e => $"dest-{e.Id}",
                    e => new DestinationConfig { Address = e.Destination })
            }).ToList();

        _configProvider.Update(routes, clusters);
    }
}
