using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Gateway.Domain.Entities;

namespace Gateway.Infrastructure.Data;

public static class SeedData
{
    public static async Task SeedFromAppSettingsAsync(GatewayDbContext db, IConfiguration config)
    {
        if (await db.Groups.AnyAsync())
            return; // Already seeded

        var proxySection = config.GetSection("ReverseProxy");
        var routesSection = proxySection.GetSection("Routes");
        var clustersSection = proxySection.GetSection("Clusters");

        foreach (var routeChild in routesSection.GetChildren())
        {
            var routeId = routeChild.Key;
            var clusterId = routeChild["ClusterId"] ?? routeId;
            var path = routeChild.GetSection("Match")["Path"] ?? "";
            var authPolicy = routeChild["AuthorizationPolicy"];
            var transforms = routeChild.GetSection("Transforms");
            string? removePrefix = null;

            foreach (var transform in transforms.GetChildren())
            {
                removePrefix = transform["PathRemovePrefix"];
                if (removePrefix != null) break;
            }

            var clusterSection = clustersSection.GetSection(clusterId);
            var destinations = clusterSection.GetSection("Destinations");

            var group = new ProxyGroup
            {
                Name = clusterId,
                Description = $"Auto-imported from appsettings.json: {routeId}",
                IsEnabled = true
            };
            db.Groups.Add(group);
            await db.SaveChangesAsync();

            foreach (var destChild in destinations.GetChildren())
            {
                var destName = destChild.Key;
                var address = destChild["Address"] ?? "http://localhost:5000";

                var endpoint = new ProxyEndpoint
                {
                    GroupId = group.Id,
                    Name = $"{routeId}/{destName}",
                    PathPattern = path,
                    Destination = address,
                    RemovePrefix = removePrefix,
                    RequiresAuth = !string.IsNullOrEmpty(authPolicy),
                    IsEnabled = true
                };
                db.Endpoints.Add(endpoint);
            }
            await db.SaveChangesAsync();
        }
    }
}
