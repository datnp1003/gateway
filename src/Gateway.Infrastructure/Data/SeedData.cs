using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Gateway.Domain.Entities;

namespace Gateway.Infrastructure.Data;

public static class SeedData
{
    public static async Task EnsureSchemaAsync(GatewayDbContext db)
    {
        if (!await HasColumnAsync(db, "Endpoints", "RateLimitPerMinute"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Endpoints ADD COLUMN RateLimitPerMinute INTEGER NULL");
        if (!await HasColumnAsync(db, "Endpoints", "BlockedIpRanges"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Endpoints ADD COLUMN BlockedIpRanges TEXT NULL");
        if (!await HasColumnAsync(db, "Endpoints", "AllowedIpRanges"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Endpoints ADD COLUMN AllowedIpRanges TEXT NULL");
        if (!await HasColumnAsync(db, "Groups", "BlockedIpRanges"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Groups ADD COLUMN BlockedIpRanges TEXT NULL");
        if (!await HasColumnAsync(db, "Groups", "AllowedIpRanges"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Groups ADD COLUMN AllowedIpRanges TEXT NULL");
    }

    private static async Task<bool> HasColumnAsync(GatewayDbContext db, string table, string column)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static async Task SeedFromAppSettingsAsync(GatewayDbContext db, IConfiguration config)
    {
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
                    RequiresAuth = !string.IsNullOrEmpty(authPolicy),
                    IsEnabled = true
                };
                db.Endpoints.Add(endpoint);
            }
        }

        await db.SaveChangesAsync();
    }
}
