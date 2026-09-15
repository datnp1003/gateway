using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Gateway.Domain.Entities;

namespace Gateway.Infrastructure.Data;

public static class SeedData
{
    public static async Task SeedFromAppSettingsAsync(GatewayDbContext db, IConfiguration config)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serialize first-run imports across gateway instances; rollback partial seeds.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Groups\" IN SHARE ROW EXCLUSIVE MODE");
        if (await db.Groups.AnyAsync())
            return; // Already seeded

        var proxySection = config.GetSection("ReverseProxy");
        var routesSection = proxySection.GetSection("Routes");
        var clustersSection = proxySection.GetSection("Clusters");

        // Create a single "api" group with path "api"
        var apiGroup = new ProxyGroup
        {
            Name = "api",
            Path = "api",
            Description = "Auto-imported API group from appsettings.json",
            IsEnabled = true
        };
        db.Groups.Add(apiGroup);
        await db.SaveChangesAsync();

        // Map each route into an endpoint under the "api" group
        foreach (var routeChild in routesSection.GetChildren())
        {
            var routeId = routeChild.Key;
            var clusterId = routeChild["ClusterId"] ?? routeId;
            var fullPath = routeChild.GetSection("Match")["Path"] ?? "";
            var authPolicy = routeChild["AuthorizationPolicy"];

            // Strip the group prefix "/api" from the full path to get the endpoint PathPattern
            // e.g. "/api/auth/{**catch-all}" -> "/auth/{**catch-all}"
            const string groupPrefix = "/api";
            var pathPattern = fullPath.StartsWith(groupPrefix)
                ? fullPath[groupPrefix.Length..]
                : fullPath;

            // If pathPattern is empty after stripping, default to "/{**catch-all}"
            if (string.IsNullOrEmpty(pathPattern))
                pathPattern = "/{**catch-all}";

            // Preserve the route's PathRemovePrefix transform, if any; otherwise
            // YarpConfigSyncService falls back to stripping the group path.
            string? removePrefix = null;
            foreach (var transform in routeChild.GetSection("Transforms").GetChildren())
            {
                var value = transform["PathRemovePrefix"];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    removePrefix = value;
                    break;
                }
            }

            var clusterSection = clustersSection.GetSection(clusterId);
            var destinations = clusterSection.GetSection("Destinations");

            foreach (var destChild in destinations.GetChildren())
            {
                var address = destChild["Address"] ?? "http://localhost:5000";

                var endpoint = new ProxyEndpoint
                {
                    GroupId = apiGroup.Id,
                    Name = routeId,
                    PathPattern = pathPattern,
                    Destination = address,
                    RemovePrefix = removePrefix,

                    IsEnabled = true
                };
                db.Endpoints.Add(endpoint);
            }
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
