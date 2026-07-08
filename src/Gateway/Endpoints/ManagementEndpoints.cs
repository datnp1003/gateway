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
        var api = app.MapGroup("/api/management");

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

        api.MapGet("/dashboard", (IProxyConfigProvider configProvider, ILogBuffer logs,
            IMetricsTracker metrics) =>
        {
            var config = configProvider.GetConfig();
            return Results.Ok(new DashboardData(
                StartedAt: DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64),
                Uptime: TimeSpan.FromMilliseconds(Environment.TickCount64),
                TotalRequests: metrics.GetMetrics().TotalRequests,
                ActiveConnections: 0,
                Routes: config.Routes.Select(r => new RouteInfo(
                    r.RouteId, r.ClusterId ?? "?", r.Match.Path ?? "?",
                    r.AuthorizationPolicy,
                    r.Transforms?.Select(t => t.GetType().Name).ToList() ?? new())).ToList(),
                Clusters: config.Clusters.Select(c => new ClusterInfo(
                    c.ClusterId,
                    c.Destinations?.Select((kvp, _) => new DestinationInfo(kvp.Key, kvp.Value.Address, null)).ToList() ?? new())).ToList(),
                RecentLogs: logs.GetRecent(50)
            ));
        });

        // ─── Groups CRUD ───
        var groups = app.MapGroup("/api/management/groups");

        groups.MapGet("/", async (IProxyConfigRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAllGroupsAsync(ct)));

        groups.MapGet("/{id:guid}", async (Guid id, IProxyConfigRepository repo, CancellationToken ct) =>
        {
            var group = await repo.GetGroupByIdAsync(id, ct);
            return group is null ? Results.NotFound() : Results.Ok(group);
        });

        groups.MapPost("/", async (CreateGroupRequest req, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            var group = new ProxyGroup { Name = req.Name, Path = req.Path, Description = req.Description };
            var created = await repo.CreateGroupAsync(group, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Created($"/api/management/groups/{created.Id}", created);
        });

        groups.MapPut("/{id:guid}", async (Guid id, UpdateGroupRequest req, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            var existing = await repo.GetGroupByIdAsync(id, ct);
            if (existing is null) return Results.NotFound();
            if (req.Name != null) existing.Name = req.Name;
            if (req.Path != null) existing.Path = req.Path;
            if (req.Description != null) existing.Description = req.Description;
            if (req.IsEnabled.HasValue) existing.IsEnabled = req.IsEnabled.Value;
            var updated = await repo.UpdateGroupAsync(existing, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Ok(updated);
        });

        groups.MapDelete("/{id:guid}", async (Guid id, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            await repo.DeleteGroupAsync(id, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.NoContent();
        });

        // ─── Endpoints CRUD ───
        var endpoints = app.MapGroup("/api/management/endpoints");

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
            var endpoint = new ProxyEndpoint
            {
                GroupId = req.GroupId,
                Name = req.Name,
                PathPattern = req.PathPattern,
                Destination = req.Destination,
                RemovePrefix = req.RemovePrefix,
                RequiresAuth = req.RequiresAuth
            };
            var created = await repo.CreateEndpointAsync(endpoint, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Created($"/api/management/endpoints/{created.Id}", created);
        });

        endpoints.MapPut("/{id:guid}", async (Guid id, UpdateEndpointRequest req, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            var existing = await repo.GetEndpointsByGroupAsync(Guid.Empty, ct);
            var ep = existing.FirstOrDefault(e => e.Id == id);
            if (ep is null)
            {
                // Try to find it by loading all groups
                var allGroups = await repo.GetAllGroupsAsync(ct);
                ep = allGroups.SelectMany(g => g.Endpoints).FirstOrDefault(e => e.Id == id);
            }
            if (ep is null) return Results.NotFound();
            if (req.Name != null) ep.Name = req.Name;
            if (req.PathPattern != null) ep.PathPattern = req.PathPattern;
            if (req.Destination != null) ep.Destination = req.Destination;
            if (req.RemovePrefix != null) ep.RemovePrefix = req.RemovePrefix;
            if (req.RequiresAuth.HasValue) ep.RequiresAuth = req.RequiresAuth.Value;
            if (req.IsEnabled.HasValue) ep.IsEnabled = req.IsEnabled.Value;
            var updated = await repo.UpdateEndpointAsync(ep, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Ok(updated);
        });

        endpoints.MapDelete("/{id:guid}", async (Guid id, IProxyConfigRepository repo, IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            await repo.DeleteEndpointAsync(id, ct);
            await sync.SyncFromDatabaseAsync(ct);
            return Results.NoContent();
        });

        // ─── Force Sync ───
        app.MapPost("/api/management/sync", async (IYarpConfigSyncService sync, CancellationToken ct) =>
        {
            await sync.SyncFromDatabaseAsync(ct);
            return Results.Ok(new { message = "YARP synced from database" });
        });
    }
}

// ─── DTOs for CRUD ───
public record CreateGroupRequest(string Name, string Path, string? Description);
public record UpdateGroupRequest(string? Name, string? Path, string? Description, bool? IsEnabled);
public record CreateEndpointRequest(
    Guid GroupId, string Name, string PathPattern, string Destination,
    string? RemovePrefix, bool RequiresAuth = false);
public record UpdateEndpointRequest(
    string? Name, string? PathPattern, string? Destination,
    string? RemovePrefix, bool? RequiresAuth, bool? IsEnabled);
