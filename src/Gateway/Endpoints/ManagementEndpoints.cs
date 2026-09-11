using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Yarp.ReverseProxy.Configuration;
using Gateway.Infrastructure.Services;
using Gateway.Domain.Interfaces;
using Gateway.Domain.Models;
using Gateway.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Endpoints;

public static class ManagementEndpoints
{
    public static void MapManagementApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/management")
            .RequireRateLimiting("management")
            .RequireAuthorization(Gateway.Authentication.ManagementAuth.Policy);

        api.MapGet("/health", (IMetricsTracker metrics) =>
        {
            return Results.Ok(new
            {
                status = "healthy",
                uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(),
                metrics = metrics.GetMetrics()
            });
        });

        api.MapGet("/routes", (IProxyConfigProvider configProvider) =>
        {
            var config = configProvider.GetConfig();
            var routes = config.Routes.Select(r => new RouteInfo(
                r.RouteId,
                r.ClusterId ?? "?",
                r.Match.Path ?? "?",
                r.AuthorizationPolicy,
                r.Transforms?.Select(t => t.GetType().Name).ToList() ?? new()
            ));
            return Results.Ok(routes);
        });

        api.MapGet("/clusters", (IProxyConfigProvider configProvider) =>
        {
            var config = configProvider.GetConfig();
            var clusters = config.Clusters.Select(c => new ClusterInfo(
                c.ClusterId,
                c.Destinations?.Select((kvp, _) => new DestinationInfo(
                    kvp.Key, kvp.Value.Address, "unknown")).ToList() ?? new()
            ));
            return Results.Ok(clusters);
        });

        api.MapGet("/logs", (ILogBuffer logs, int count = 50) =>
            Results.Ok(logs.GetRecent(Math.Clamp(count, 1, 500))));

        api.MapGet("/metrics", (IMetricsTracker metrics) =>
            Results.Ok(metrics.GetMetrics()));

        api.MapGet("/metrics/history", (IMetricsTracker metrics, int count = 60) =>
            Results.Ok(metrics.GetHistory(Math.Clamp(count, 1, 300))));

        api.MapGet("/dashboard", (IProxyConfigProvider configProvider, ILogBuffer logs,
            IMetricsTracker metrics) =>
        {
            var config = configProvider.GetConfig();
            var m = metrics.GetMetrics();
            return Results.Ok(new
            {
                StartedAt = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64),
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(),
                TotalRequests = m.TotalRequests,
                RequestsPerSecond = m.RequestsPerSecond,
                ErrorRate = m.ErrorRate,
                AvgLatencyMs = m.AvgLatencyMs,
                P95LatencyMs = m.P95LatencyMs,
                ActiveRequests = m.ActiveRequests,
                TopRoutes = m.TopRoutes,
                TopStatusCodes = m.TopStatusCodes,
                StatusCodes = m.StatusCodes,
                Routes = config.Routes.Select(r => new RouteInfo(
                    r.RouteId, r.ClusterId ?? "?", r.Match.Path ?? "?",
                    r.AuthorizationPolicy,
                    r.Transforms?.Select(t => t.GetType().Name).ToList() ?? new())).ToList(),
                Clusters = config.Clusters.Select(c => new ClusterInfo(
                    c.ClusterId,
                    c.Destinations?.Select((kvp, _) => new DestinationInfo(kvp.Key, kvp.Value.Address, null)).ToList() ?? new())).ToList(),
                RecentLogs = logs.GetRecent(50)
            });
        });

        // ─── Groups CRUD ───
        var groups = api.MapGroup("/groups");

        groups.MapGet("/", async (IProxyConfigRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAllGroupsAsync(ct)));

        groups.MapGet("/{id:guid}", async (Guid id, IProxyConfigRepository repo, CancellationToken ct) =>
        {
            var group = await repo.GetGroupByIdAsync(id, ct);
            return group is null ? Results.NotFound() : Results.Ok(group);
        });

        groups.MapPost("/", async (CreateGroupRequest req, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            if (ValidateGroupFields(req.Name, req.Path, isCreate: true) is { } error)
                return Results.BadRequest(new { error });

            var group = new ProxyGroup
            {
                Name = req.Name,
                Path = req.Path,
                Description = req.Description,
                BlockedIpRanges = req.BlockedIpRanges,
                AllowedIpRanges = req.AllowedIpRanges,
            };
            ProxyGroup created;
            try
            {
                created = await repo.CreateGroupAsync(group, ct);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "A group with the same name or path already exists." });
            }
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Created($"/api/management/groups/{created.Id}", created);
        });

        groups.MapPut("/{id:guid}", async (Guid id, UpdateGroupRequest req, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            if (ValidateGroupFields(req.Name, req.Path, isCreate: false) is { } error)
                return Results.BadRequest(new { error });

            var existing = await repo.GetGroupByIdAsync(id, ct);
            if (existing is null) return Results.NotFound();
            if (req.Name != null) existing.Name = req.Name;
            if (req.Path != null) existing.Path = req.Path;
            if (req.Description != null) existing.Description = req.Description;
            if (req.IsEnabled.HasValue) existing.IsEnabled = req.IsEnabled.Value;
            // null = leave unchanged; empty/whitespace string = clear the policy
            if (req.BlockedIpRanges != null)
                existing.BlockedIpRanges = string.IsNullOrWhiteSpace(req.BlockedIpRanges) ? null : req.BlockedIpRanges;
            if (req.AllowedIpRanges != null)
                existing.AllowedIpRanges = string.IsNullOrWhiteSpace(req.AllowedIpRanges) ? null : req.AllowedIpRanges;
            ProxyGroup updated;
            try
            {
                updated = await repo.UpdateGroupAsync(existing, ct);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "A group with the same name or path already exists." });
            }
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Ok(updated);
        });

        groups.MapDelete("/{id:guid}", async (Guid id, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            try
            {
                await repo.DeleteGroupAsync(id, ct);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            await sync.SyncFromDatabaseAsync(ct);
            return Results.NoContent();
        });

        // ─── Endpoints CRUD ───
        var endpoints = api.MapGroup("/endpoints");

        endpoints.MapGet("/", async (Guid? groupId, IProxyConfigRepository repo, CancellationToken ct) =>
        {
            if (groupId.HasValue)
                return Results.Ok(await repo.GetEndpointsByGroupAsync(groupId.Value, ct));
            // If no groupId, return all groups with their endpoints
            var groups = await repo.GetAllGroupsAsync(ct);
            return Results.Ok(groups.SelectMany(g => g.Endpoints));
        });

        endpoints.MapPost("/", async (CreateEndpointRequest req, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            if (ValidateEndpointFields(req.Name, req.PathPattern, req.Destination,
                    req.RemovePrefix, req.RateLimitPerMinute, isCreate: true) is { } error)
                return Results.BadRequest(new { error });
            if (await repo.GetGroupByIdAsync(req.GroupId, ct) is null)
                return Results.BadRequest(new { error = $"Group {req.GroupId} does not exist." });
            // No DB unique index on endpoints (seeding can legitimately produce
            // duplicates), so uniqueness is enforced here at the API boundary.
            var siblings = await repo.GetEndpointsByGroupAsync(req.GroupId, ct);
            if (siblings.Any(e => e.PathPattern == req.PathPattern))
                return Results.Conflict(new { error = "An endpoint with the same path pattern already exists in this group." });

            var endpoint = new ProxyEndpoint
            {
                GroupId = req.GroupId,
                Name = req.Name,
                PathPattern = req.PathPattern,
                Destination = req.Destination,
                RemovePrefix = req.RemovePrefix,
                RequiresAuth = req.RequiresAuth,
                RateLimitPerMinute = req.RateLimitPerMinute,
                BlockedIpRanges = req.BlockedIpRanges,
                AllowedIpRanges = req.AllowedIpRanges,
            };
            var created = await repo.CreateEndpointAsync(endpoint, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Created($"/api/management/endpoints/{created.Id}", created);
        });

        endpoints.MapPut("/{id:guid}", async (Guid id, UpdateEndpointRequest req, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            if (ValidateEndpointFields(req.Name, req.PathPattern, req.Destination,
                    req.RemovePrefix, req.RateLimitPerMinute, isCreate: false) is { } error)
                return Results.BadRequest(new { error });

            var existing = await repo.GetEndpointsByGroupAsync(Guid.Empty, ct);
            var ep = existing.FirstOrDefault(e => e.Id == id);
            if (ep is null)
            {
                // Try to find it by loading all groups
                var allGroups = await repo.GetAllGroupsAsync(ct);
                ep = allGroups.SelectMany(g => g.Endpoints).FirstOrDefault(e => e.Id == id);
            }
            if (ep is null) return Results.NotFound();
            if (req.PathPattern != null)
            {
                var siblings = await repo.GetEndpointsByGroupAsync(ep.GroupId, ct);
                if (siblings.Any(e => e.Id != id && e.PathPattern == req.PathPattern))
                    return Results.Conflict(new { error = "An endpoint with the same path pattern already exists in this group." });
            }
            if (req.Name != null) ep.Name = req.Name;
            if (req.PathPattern != null) ep.PathPattern = req.PathPattern;
            if (req.Destination != null) ep.Destination = req.Destination;
            if (req.RemovePrefix != null) ep.RemovePrefix = req.RemovePrefix;
            if (req.RequiresAuth.HasValue) ep.RequiresAuth = req.RequiresAuth.Value;
            if (req.IsEnabled.HasValue) ep.IsEnabled = req.IsEnabled.Value;
            // Same partial-update contract as groups: null = leave unchanged;
            // empty string (or zero for the rate limit) = clear the policy.
            if (req.RateLimitPerMinute.HasValue)
                ep.RateLimitPerMinute = req.RateLimitPerMinute.Value == 0 ? null : req.RateLimitPerMinute;
            if (req.BlockedIpRanges != null)
                ep.BlockedIpRanges = string.IsNullOrWhiteSpace(req.BlockedIpRanges) ? null : req.BlockedIpRanges;
            if (req.AllowedIpRanges != null)
                ep.AllowedIpRanges = string.IsNullOrWhiteSpace(req.AllowedIpRanges) ? null : req.AllowedIpRanges;
            var updated = await repo.UpdateEndpointAsync(ep, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Ok(updated);
        });

        endpoints.MapDelete("/{id:guid}", async (Guid id, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            try
            {
                await repo.DeleteEndpointAsync(id, ct);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            await sync.SyncFromDatabaseAsync(ct);
            return Results.NoContent();
        });

        // ─── Force Sync ───
        api.MapPost("/sync", async (IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Ok(new { message = "YARP synced from database" });
        });
    }

    // ─── Minimal boundary validation (returns an error message, or null if valid) ───
    // On create the fields are required; on update only provided (non-null)
    // fields are checked, matching the partial-update contract.

    private static string? ValidateGroupFields(string? name, string? path, bool isCreate)
    {
        if (isCreate && string.IsNullOrWhiteSpace(name))
            return "name is required.";
        if (name is not null && (string.IsNullOrWhiteSpace(name) || name.Length > 200))
            return "name must be a non-empty string of at most 200 characters.";
        if (isCreate && string.IsNullOrWhiteSpace(path))
            return "path is required.";
        if (path is not null &&
            (string.IsNullOrWhiteSpace(path) || path.Length > 200 ||
             path.Any(char.IsWhiteSpace) || path.Contains('{') || path.Contains('}')))
            return "path must be non-empty, at most 200 characters, without whitespace or braces.";
        return null;
    }

    private static string? ValidateEndpointFields(
        string? name, string? pathPattern, string? destination,
        string? removePrefix, int? rateLimitPerMinute, bool isCreate)
    {
        if (isCreate && string.IsNullOrWhiteSpace(name))
            return "name is required.";
        if (name is not null && (string.IsNullOrWhiteSpace(name) || name.Length > 200))
            return "name must be a non-empty string of at most 200 characters.";
        if (isCreate && string.IsNullOrWhiteSpace(pathPattern))
            return "pathPattern is required.";
        if (pathPattern is not null && (!pathPattern.StartsWith('/') || pathPattern.Length > 500))
            return "pathPattern must start with '/' and be at most 500 characters.";
        if (isCreate && string.IsNullOrWhiteSpace(destination))
            return "destination is required.";
        if (destination is not null &&
            (!Uri.TryCreate(destination, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            return "destination must be an absolute http:// or https:// URL.";
        if (!string.IsNullOrEmpty(removePrefix) && !removePrefix.StartsWith('/'))
            return "removePrefix must start with '/'.";
        // 0 clears the limit on update; a configured limit must be 1..1,000,000.
        var minRateLimit = isCreate ? 1 : 0;
        if (rateLimitPerMinute is { } limit && (limit < minRateLimit || limit > 1_000_000))
            return $"rateLimitPerMinute must be between {minRateLimit} and 1000000.";
        return null;
    }
}

// ─── DTOs for CRUD ───
public record CreateGroupRequest(
    string Name, string Path, string? Description,
    string? BlockedIpRanges = null,
    string? AllowedIpRanges = null);
public record UpdateGroupRequest(
    string? Name, string? Path, string? Description, bool? IsEnabled,
    string? BlockedIpRanges = null,
    string? AllowedIpRanges = null);
public record CreateEndpointRequest(
    Guid GroupId, string Name, string PathPattern, string Destination,
    string? RemovePrefix, bool RequiresAuth = false,
    int? RateLimitPerMinute = null,
    string? BlockedIpRanges = null,
    string? AllowedIpRanges = null);
public record UpdateEndpointRequest(
    string? Name, string? PathPattern, string? Destination,
    string? RemovePrefix, bool? RequiresAuth, bool? IsEnabled,
    int? RateLimitPerMinute = null,
    string? BlockedIpRanges = null,
    string? AllowedIpRanges = null);
