using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Yarp.ReverseProxy.Configuration;
using Gateway.Infrastructure.Services;
using Gateway.Domain.Interfaces;
using Gateway.Domain.Models;

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
    }
}
