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
            var transforms = new List<IReadOnlyDictionary<string, string>>();
            if (!string.IsNullOrEmpty(e.RemovePrefix))
            {
                transforms.Add(new Dictionary<string, string>
                {
                    { "PathRemovePrefix", e.RemovePrefix }
                });
            }

            return new RouteConfig
            {
                RouteId = $"route-{e.Id}",
                ClusterId = $"cluster-{e.GroupId}",
                Match = new RouteMatch { Path = e.PathPattern },
                AuthorizationPolicy = e.RequiresAuth ? "Authenticated" : null,
                Transforms = transforms.Count > 0 ? transforms : null
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
