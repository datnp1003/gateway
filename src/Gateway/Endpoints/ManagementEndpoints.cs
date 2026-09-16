using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Yarp.ReverseProxy.Configuration;
using Gateway.Infrastructure.Services;
using Gateway.Domain.Interfaces;
using Gateway.Domain.Models;
using Gateway.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Gateway.Infrastructure.Data;
using System.Text;
using System.Text.Json;

namespace Gateway.Endpoints;

public static partial class ManagementEndpoints
{
    public static void MapManagementApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/management")
            .RequireCors("GatewayManagement")
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

        api.MapGet("/logs/page", (int page = 1, int pageSize = 25, string? level = null,
            string? search = null, int? status = null, DateTimeOffset? before = null) =>
        {
            try { return Results.Ok(RetainedLogs.Query(Path.GetFullPath("logs"), page, pageSize, level, search, status, before)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (IOException) { return Results.Problem("Retained logs changed or could not be read. Retry the query.", statusCode: 503); }
            catch (UnauthorizedAccessException) { return Results.Problem("Retained logs are not readable.", statusCode: 503); }
        });

        api.MapGet("/operations/ingestion", (IProxyAttemptSink sink) => Results.Ok(sink.GetState()));

        api.MapGet("/operations/configuration", async (IProxyConfigRepository repo, CancellationToken ct) =>
        {
            var groups = await repo.GetAllGroupsAsync(ct);
            return Results.Ok(groups.Select(group => new
            {
                id = group.Id, name = group.Name, path = group.Path, isEnabled = group.IsEnabled,
                endpoints = group.Endpoints.Select(endpoint => new { id = endpoint.Id, name = endpoint.Name, pathPattern = endpoint.PathPattern, destination = SafeDestination(endpoint.Destination), isEnabled = endpoint.IsEnabled })
            }));
        });

        api.MapGet("/operations/events", async (GatewayDbContext db, DateTimeOffset from, DateTimeOffset to, int limit = 50,
            string? cursor = null, Guid? groupId = null, Guid? endpointId = null, string? outcome = null, bool failureOnly = false,
            string? search = null, int? status = null, CancellationToken ct = default) =>
        {
            if (from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero || from >= to || to - from > TimeSpan.FromDays(7) || limit is < 1 or > 100 ||
                (outcome is not null && outcome is not ("upstream_response" or "gateway_rejected" or "network_failure" or "gateway_failure" or "client_disconnected")) || status is < 100 or > 599)
                return Results.BadRequest(new { error = "Invalid UTC window or filters." });
            var filter = new OperationsFilter(from.UtcDateTime, to.UtcDateTime, groupId, endpointId, outcome, failureOnly, search?.Trim(), status);
            CursorState? state = null;
            if (cursor is not null)
            {
                try { state = JsonSerializer.Deserialize<CursorState>(Encoding.UTF8.GetString(Convert.FromBase64String(cursor))); }
                catch { return Results.BadRequest(new { error = "Invalid cursor." }); }
                if (state is null || state.Filter != filter) return Results.BadRequest(new { error = "Cursor does not match normalized filters." });
            }
            var query = db.ProxyRequestEvents.AsNoTracking().Where(item => item.OccurredAt >= filter.From && item.OccurredAt < filter.To);
            if (filter.GroupId is { } group) query = query.Where(item => item.GroupId == group);
            if (filter.EndpointId is { } endpoint) query = query.Where(item => item.EndpointId == endpoint);
            if (filter.Outcome is not null) query = query.Where(item => item.Outcome == filter.Outcome);
            if (filter.Status is { } responseStatus) query = query.Where(item => item.ResponseStatus == responseStatus);
            if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(item => EF.Functions.ILike(item.RequestPath, $"%{filter.Search}%") || EF.Functions.ILike(item.EndpointName, $"%{filter.Search}%") || EF.Functions.ILike(item.GroupName, $"%{filter.Search}%"));
            if (filter.FailureOnly) query = query.Where(item => item.Outcome == "gateway_rejected" || item.Outcome == "network_failure" || item.Outcome == "gateway_failure" || (item.Outcome == "upstream_response" && item.ResponseStatus >= 400));
            if (state is not null) query = query.Where(item => item.OccurredAt < state.LastOccurredAt || (item.OccurredAt == state.LastOccurredAt && item.Id.CompareTo(state.LastId) < 0));
            var items = await query.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id).Take(limit + 1).ToListAsync(ct);
            var hasNext = items.Count > limit;
            if (hasNext) items.RemoveAt(limit);
            var last = items.LastOrDefault();
            var nextCursor = hasNext && last is not null ? Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CursorState(filter, last.OccurredAt, last.Id)))) : null;
            return Results.Ok(new { items = items.Select(EventDto), nextCursor, hasNext, consistency = "keyset pagination prevents duplicate already-seen rows; late database inserts with older timestamps may appear on later pages" });
        });

        api.MapGet("/operations/summary", async (ProxyOperationsQueryService queries, string period = "24h", Guid? groupId = null, Guid? endpointId = null, CancellationToken ct = default) =>
        {
            var window = period switch { "1h" => TimeSpan.FromHours(1), "24h" => TimeSpan.FromHours(24), "7d" => TimeSpan.FromDays(7), _ => TimeSpan.Zero };
            if (window == TimeSpan.Zero) return Results.BadRequest(new { error = "period must be 1h, 24h, or 7d." });
            var end = DateTime.UtcNow; var start = end - window;
            var bucket = period == "1h" ? TimeSpan.FromSeconds(60) : period == "24h" ? TimeSpan.FromSeconds(300) : TimeSpan.FromSeconds(3600);
            var summary = await queries.GetSummaryAsync(start, end, bucket, groupId, endpointId, ct);
            var current = summary.Current;
            return Results.Ok(new { period, from = start, to = end, attempts = current.Attempts, attemptRatePerSecond = current.Attempts / window.TotalSeconds, failedRequests = current.FailedRequests,
                failureRatePercent = current.Attempts == 0 ? 0 : current.FailedRequests * 100d / current.Attempts, gatewayRejected = current.GatewayRejected, networkFailures = current.NetworkFailures, clientDisconnected = current.ClientDisconnected, upstream4xx = current.Upstream4xx, upstream5xx = current.Upstream5xx,
                latency = current.AverageLatencyMs is null ? null : new { averageMs = current.AverageLatencyMs, p95Ms = current.P95LatencyMs }, previous = new { available = summary.Previous is not null, summary = summary.Previous is null ? null : MetricsDto(summary.Previous) },
                series = summary.Series.Select(item => new { from = item.From, to = item.To, attempts = item.Attempts, failedRequests = item.FailedRequests, latency = item.AverageLatencyMs is null ? null : new { averageMs = item.AverageLatencyMs, p95Ms = item.P95LatencyMs } }),
                rankings = summary.Rankings.Select(item => new { endpointId = item.EndpointId, endpointName = item.EndpointName, attempts = item.Attempts, failedRequests = item.FailedRequests, p95LatencyMs = item.P95LatencyMs }), data = new { observedEarliestEventAt = summary.ObservedEarliestEventAt, collectionStart = (DateTime?)null, coverage = "observed retained events only; collection start is unavailable after restart" } });
        });

        api.MapGet("/operations/overview", async (ProxyOperationsQueryService queries, string? timezone = null, CancellationToken ct = default) =>
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(timezone ?? "UTC"); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return Results.BadRequest(new { error = "Invalid timezone." }); }
            var now = DateTime.UtcNow;
            var localDay = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;
            var todayStart = TimeZoneInfo.ConvertTimeToUtc(localDay, zone);
            var yesterdayStart = TimeZoneInfo.ConvertTimeToUtc(localDay.AddDays(-1), zone);
            var tomorrowStart = TimeZoneInfo.ConvertTimeToUtc(localDay.AddDays(1), zone);
            var overview = await queries.GetOverviewAsync(now, ct, todayStart, yesterdayStart);
            static object Metrics(OperationsMetrics value) => new { attempts = value.Attempts, failedRequests = value.FailedRequests, networkFailures = value.NetworkFailures, averageLatencyMs = value.AverageLatencyMs, uniqueClients = value.UniqueClients };
            return Results.Ok(new
            {
                generatedAt = now, timezone = zone.Id, windows = new { recentMinute = new { from = now.AddMinutes(-1), to = now }, previousMinute = new { from = now.AddMinutes(-2), to = now.AddMinutes(-1) }, today = new { from = todayStart, to = now }, yesterday = new { from = yesterdayStart, to = todayStart }, traffic = new { from = todayStart, to = tomorrowStart, bucket = "1h" }, statusErrors = new { from = now.AddHours(-1), to = now }, recentEndpoints = new { from = now.AddMinutes(-5), to = now }, groups = new { from = now.AddHours(-1), to = now }, groupsHourly = new { from = now.AddHours(-1), to = now, bucket = "1m" } },
                requests = new { currentMinute = Metrics(overview.CurrentMinute), previousMinute = Metrics(overview.PreviousMinute), today = Metrics(overview.Today), yesterday = Metrics(overview.Yesterday) },
                traffic = overview.Traffic.Select(item => new { from = item.From, to = item.To, attempts = item.Attempts, uniqueClients = item.UniqueClients }),
                statusErrors = overview.StatusErrors.Select(item => new { status = item.Status, attempts = item.Attempts }), networkFailuresLastHour = overview.LastHour.NetworkFailures,
                recentEndpoints = overview.RecentEndpoints.Select(item => new { endpointId = item.EndpointId, method = item.Method, path = item.RequestPath, attempts = item.Attempts, averageLatencyMs = item.AverageLatencyMs, latestResponseStatus = item.LatestResponseStatus, latestOutcome = item.LatestOutcome }),
                groups = overview.Groups.Select(item => new { groupId = item.GroupId, groupName = item.GroupName, requestsPerMinute = item.Attempts / 60d, observedEndpoints = item.ObservedEndpoints, measuredSuccessPercent = item.Attempts == 0 ? (double?)null : item.SuccessfulResponses * 100d / item.Attempts,
                    hourly = (overview.GroupsHourly.TryGetValue(item.GroupId, out var series) ? series : Array.Empty<OperationsSeriesBucket>()).Select(bucket => new { from = bucket.From, to = bucket.To, attempts = bucket.Attempts, failedRequests = bucket.FailedRequests }) }),
                data = new { observedEarliestEventAt = overview.ObservedEarliestEventAt, coverage = "observed retained events only" }
            });
        });

        api.MapGet("/route-mappings", async (IProxyConfigProvider provider, IProxyConfigRepository repo, CancellationToken ct) =>
        {
            var config = provider.GetConfig();
            var groups = await repo.GetAllGroupsAsync(ct);
            var names = groups.SelectMany(g => g.Endpoints.Select(e => new { RouteId = $"route-{e.Id}", e.Name, Group = g.Name })).ToDictionary(e => e.RouteId);
            return Results.Ok(config.Routes.Select(r => new
            {
                r.RouteId, r.ClusterId,
                name = names.GetValueOrDefault(r.RouteId)?.Name ?? r.Match.Path,
                group = names.GetValueOrDefault(r.RouteId)?.Group,
                path = r.Match.Path,
                removePrefix = r.Transforms?.Select(t => t.GetValueOrDefault("PathRemovePrefix")).FirstOrDefault(p => p != null),
                destinations = config.Clusters.FirstOrDefault(c => c.ClusterId == r.ClusterId)?.Destinations?.Values.Select(d => d.Address).ToArray() ?? []
            }));
        });

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
    string? RemovePrefix,
    int? RateLimitPerMinute = null,
    string? BlockedIpRanges = null,
    string? AllowedIpRanges = null);
public record UpdateEndpointRequest(
    string? Name, string? PathPattern, string? Destination,
    string? RemovePrefix, bool? IsEnabled,
    int? RateLimitPerMinute = null,
    string? BlockedIpRanges = null,
    string? AllowedIpRanges = null);

public record OperationsFilter(DateTime From, DateTime To, Guid? GroupId, Guid? EndpointId, string? Outcome, bool FailureOnly, string? Search, int? Status);
public record CursorState(OperationsFilter Filter, DateTime LastOccurredAt, Guid LastId);

public static partial class ManagementEndpoints
{
    private static object EventDto(ProxyRequestEvent item) => new { id = item.Id, occurredAt = item.OccurredAt, completedAt = item.CompletedAt, durationMs = item.DurationMs, method = item.Method, requestPath = item.RequestPath, group = new { id = item.GroupId, name = item.GroupName }, endpoint = new { id = item.EndpointId, name = item.EndpointName }, configuredDestination = item.ConfiguredDestination, outcome = item.Outcome, responseStatus = item.ResponseStatus, clientIp = item.ClientIp };
    private static string SafeDestination(string destination) => Uri.TryCreate(destination, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : "invalid";
    private static object MetricsDto(OperationsMetrics metrics) => new { attempts = metrics.Attempts, failedRequests = metrics.FailedRequests, gatewayRejected = metrics.GatewayRejected, networkFailures = metrics.NetworkFailures, clientDisconnected = metrics.ClientDisconnected, upstream4xx = metrics.Upstream4xx, upstream5xx = metrics.Upstream5xx, latency = metrics.AverageLatencyMs is null ? null : new { averageMs = metrics.AverageLatencyMs, p95Ms = metrics.P95LatencyMs } };
}
