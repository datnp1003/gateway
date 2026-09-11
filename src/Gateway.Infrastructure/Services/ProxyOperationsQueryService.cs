using System.Data;
using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Gateway.Infrastructure.Services;

public sealed class ProxyOperationsQueryService
{
    private readonly GatewayDbContext _db;
    public ProxyOperationsQueryService(GatewayDbContext db) => _db = db;

    public async Task<OperationsSummary> GetSummaryAsync(DateTime start, DateTime end, TimeSpan bucket, Guid? groupId, Guid? endpointId, CancellationToken ct)
    {
        var previousStart = start - (end - start);
        var current = await ReadMetricsAsync(start, end, groupId, endpointId, ct);
        var previous = await ReadMetricsAsync(previousStart, start, groupId, endpointId, ct);
        var series = await ReadSeriesAsync(start, end, bucket, groupId, endpointId, ct);
        var rankings = await ReadRankingsAsync(start, end, groupId, endpointId, ct);
        var observedEarliest = await ReadObservedEarliestAsync(ct);
        return new OperationsSummary(current, previous.Attempts == 0 ? null : previous, series, rankings, observedEarliest);
    }

    public async Task<OverviewOperations> GetOverviewAsync(DateTime now, CancellationToken ct)
    {
        var minute = await ReadMetricsAsync(now.AddMinutes(-1), now, null, null, ct);
        var previousMinute = await ReadMetricsAsync(now.AddMinutes(-2), now.AddMinutes(-1), null, null, ct);
        var todayStart = now.Date;
        // Calendar-day axis: buckets from 00:00 UTC today up to `now` only (elapsed hours). Future hours are not queried so no zero traffic is fabricated; the full-day axis end is advertised via the overview `windows.traffic` metadata.
        var traffic = await ReadSeriesAsync(todayStart, now, TimeSpan.FromHours(1), null, null, ct);
        var groups = await ReadGroupsAsync(now.AddMinutes(-1), now, ct);
        // One aggregate for every displayed group's 1h minute buckets (now-1h → now); no per-group query loop.
        var groupsHourly = await ReadGroupSeriesAsync(now.AddHours(-1), now, TimeSpan.FromMinutes(1), groups.Select(group => group.GroupId).ToArray(), ct);
        return new OverviewOperations(minute, previousMinute, await ReadMetricsAsync(todayStart, now, null, null, ct), await ReadMetricsAsync(todayStart.AddDays(-1), todayStart, null, null, ct), await ReadMetricsAsync(now.AddHours(-1), now, null, null, ct), traffic,
            await ReadStatusErrorsAsync(now.AddHours(-1), now, ct), await ReadRecentEndpointsAsync(now.AddMinutes(-5), now, ct), groups, groupsHourly, await ReadObservedEarliestAsync(ct));
    }

    // Deterministic minute buckets for a fixed group set in a single query: unnest(@groupIds) CROSS JOIN generate_series,
    // LEFT JOIN events scoped by GroupId. Groups with zero observed attempts still get a full, zero-filled series (no fabricated points).
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OperationsSeriesBucket>>> ReadGroupSeriesAsync(DateTime start, DateTime end, TimeSpan bucket, Guid[] groupIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, IReadOnlyList<OperationsSeriesBucket>>();
        if (groupIds.Length == 0) return result;
        const string sql = """
            WITH groups AS (SELECT unnest(@groupIds) AS "GroupId"),
                 buckets AS (SELECT series."From", series."From" + @bucket AS "To" FROM generate_series(@start, @end - @bucket, @bucket) AS series("From"))
            SELECT groups."GroupId", buckets."From", buckets."To", count(events."Id")::bigint,
                   count(events."Id") FILTER (WHERE events."Outcome" IN ('gateway_rejected','network_failure','gateway_failure') OR (events."Outcome" = 'upstream_response' AND events."ResponseStatus" >= 400))::bigint
            FROM groups CROSS JOIN buckets
            LEFT JOIN "ProxyRequestEvents" events ON events."GroupId" = groups."GroupId" AND events."OccurredAt" >= buckets."From" AND events."OccurredAt" < buckets."To"
            GROUP BY groups."GroupId", buckets."From", buckets."To" ORDER BY groups."GroupId", buckets."From"
            """;
        await using var command = await CreateCommandAsync(sql, start, end, null, null, ct, bucket);
        command.Parameters.Add("groupIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = groupIds;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var groupId = reader.GetGuid(0);
            if (result.TryGetValue(groupId, out var list) is false) result[groupId] = list = new List<OperationsSeriesBucket>();
            ((List<OperationsSeriesBucket>)list).Add(new OperationsSeriesBucket(reader.GetDateTime(1), reader.GetDateTime(2), reader.GetInt64(3), reader.GetInt64(4), null, null));
        }
        return result;
    }

    private async Task<IReadOnlyList<OperationsStatusCount>> ReadStatusErrorsAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        const string sql = """SELECT "ResponseStatus", count(*)::bigint FROM "ProxyRequestEvents" WHERE "OccurredAt" >= @start AND "OccurredAt" < @end AND "Outcome" = 'upstream_response' AND "ResponseStatus" >= 400 GROUP BY "ResponseStatus" ORDER BY count(*) DESC, "ResponseStatus" LIMIT 8""";
        var result = new List<OperationsStatusCount>();
        await using var command = await CreateCommandAsync(sql, start, end, null, null, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new OperationsStatusCount(reader.GetInt32(0), reader.GetInt64(1)));
        return result;
    }

    private async Task<IReadOnlyList<RecentEndpoint>> ReadRecentEndpointsAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        const string sql = """
            WITH filtered AS (SELECT * FROM "ProxyRequestEvents" WHERE "OccurredAt" >= @start AND "OccurredAt" < @end),
            latest AS (SELECT DISTINCT ON ("EndpointId", "Method", "RequestPath") "EndpointId", "Method", "RequestPath", "ResponseStatus", "Outcome" FROM filtered ORDER BY "EndpointId", "Method", "RequestPath", "OccurredAt" DESC, "Id" DESC)
            SELECT latest."EndpointId", latest."Method", latest."RequestPath", count(*)::bigint, avg(filtered."DurationMs"), latest."ResponseStatus", latest."Outcome"
            FROM filtered JOIN latest USING ("EndpointId", "Method", "RequestPath")
            GROUP BY latest."EndpointId", latest."Method", latest."RequestPath", latest."ResponseStatus", latest."Outcome"
            ORDER BY count(*) DESC, latest."RequestPath", latest."Method" LIMIT 8
            """;
        var result = new List<RecentEndpoint>();
        await using var command = await CreateCommandAsync(sql, start, end, null, null, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new RecentEndpoint(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetDouble(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetString(6)));
        return result;
    }

    private async Task<IReadOnlyList<OverviewGroup>> ReadGroupsAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        const string sql = """
            WITH recent AS (
                SELECT "GroupId", count(*)::bigint AS attempts,
                       count(*) FILTER (WHERE "Outcome" = 'upstream_response' AND "ResponseStatus" < 400)::bigint AS successful
                FROM "ProxyRequestEvents" WHERE "OccurredAt" >= @start AND "OccurredAt" < @end GROUP BY "GroupId"
            )
            SELECT groups."Id", groups."Name", coalesce(recent.attempts, 0), count(endpoints."Id")::int, coalesce(recent.successful, 0)
            FROM "Groups" groups
            LEFT JOIN recent ON recent."GroupId" = groups."Id"
            LEFT JOIN "Endpoints" endpoints ON endpoints."GroupId" = groups."Id"
            GROUP BY groups."Id", groups."Name", recent.attempts, recent.successful
            ORDER BY coalesce(recent.attempts, 0) DESC, groups."Id" LIMIT 5
            """;
        var result = new List<OverviewGroup>();
        await using var command = await CreateCommandAsync(sql, start, end, null, null, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new OverviewGroup(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetInt64(4)));
        return result;
    }

    private async Task<OperationsMetrics> ReadMetricsAsync(DateTime start, DateTime end, Guid? groupId, Guid? endpointId, CancellationToken ct)
    {
        const string sql = """
            SELECT count(*)::bigint, count(*) FILTER (WHERE "Outcome" IN ('gateway_rejected','network_failure','gateway_failure') OR ("Outcome" = 'upstream_response' AND "ResponseStatus" >= 400))::bigint,
                   count(*) FILTER (WHERE "Outcome" = 'gateway_rejected')::bigint, count(*) FILTER (WHERE "Outcome" = 'network_failure')::bigint,
                   count(*) FILTER (WHERE "Outcome" = 'client_disconnected')::bigint, count(*) FILTER (WHERE "Outcome" = 'upstream_response' AND "ResponseStatus" BETWEEN 400 AND 499)::bigint,
                   count(*) FILTER (WHERE "Outcome" = 'upstream_response' AND "ResponseStatus" >= 500)::bigint, avg("DurationMs"), percentile_cont(0.95) WITHIN GROUP (ORDER BY "DurationMs")
            FROM "ProxyRequestEvents" WHERE "OccurredAt" >= @start AND "OccurredAt" < @end
              AND (@groupId IS NULL OR "GroupId" = @groupId) AND (@endpointId IS NULL OR "EndpointId" = @endpointId)
            """;
        await using var command = await CreateCommandAsync(sql, start, end, groupId, endpointId, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new OperationsMetrics(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetDouble(7), reader.IsDBNull(8) ? null : reader.GetDouble(8));
    }

    private async Task<IReadOnlyList<OperationsSeriesBucket>> ReadSeriesAsync(DateTime start, DateTime end, TimeSpan bucket, Guid? groupId, Guid? endpointId, CancellationToken ct)
    {
        const string sql = """
            WITH buckets AS (SELECT series."From", series."From" + @bucket AS "To" FROM generate_series(@start, @end - @bucket, @bucket) AS series("From"))
            SELECT buckets."From", buckets."To", count(events."Id")::bigint,
                   count(events."Id") FILTER (WHERE events."Outcome" IN ('gateway_rejected','network_failure','gateway_failure') OR (events."Outcome" = 'upstream_response' AND events."ResponseStatus" >= 400))::bigint,
                   avg(events."DurationMs"), percentile_cont(0.95) WITHIN GROUP (ORDER BY events."DurationMs")
            FROM buckets LEFT JOIN "ProxyRequestEvents" events ON events."OccurredAt" >= buckets."From" AND events."OccurredAt" < buckets."To" AND (@groupId IS NULL OR events."GroupId" = @groupId) AND (@endpointId IS NULL OR events."EndpointId" = @endpointId)
            GROUP BY buckets."From", buckets."To" ORDER BY buckets."From"
            """;
        var result = new List<OperationsSeriesBucket>();
        await using var command = await CreateCommandAsync(sql, start, end, groupId, endpointId, ct, bucket);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new OperationsSeriesBucket(reader.GetDateTime(0), reader.GetDateTime(1), reader.GetInt64(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetDouble(4), reader.IsDBNull(5) ? null : reader.GetDouble(5)));
        return result;
    }

    private async Task<IReadOnlyList<OperationsRanking>> ReadRankingsAsync(DateTime start, DateTime end, Guid? groupId, Guid? endpointId, CancellationToken ct)
    {
        const string sql = """
            WITH filtered AS (SELECT * FROM "ProxyRequestEvents" WHERE "OccurredAt" >= @start AND "OccurredAt" < @end AND (@groupId IS NULL OR "GroupId" = @groupId) AND (@endpointId IS NULL OR "EndpointId" = @endpointId)),
            representatives AS (SELECT DISTINCT ON ("EndpointId") "EndpointId", "EndpointName" FROM filtered ORDER BY "EndpointId", "OccurredAt" DESC, "Id" DESC, "EndpointName" DESC)
            SELECT filtered."EndpointId", max(representatives."EndpointName"), count(*)::bigint,
                   count(*) FILTER (WHERE filtered."Outcome" IN ('gateway_rejected','network_failure','gateway_failure') OR (filtered."Outcome" = 'upstream_response' AND filtered."ResponseStatus" >= 400))::bigint,
                   percentile_cont(0.95) WITHIN GROUP (ORDER BY filtered."DurationMs")
            FROM filtered JOIN representatives ON representatives."EndpointId" = filtered."EndpointId" GROUP BY filtered."EndpointId" ORDER BY count(*) DESC, filtered."EndpointId" LIMIT 10
            """;
        var result = new List<OperationsRanking>();
        await using var command = await CreateCommandAsync(sql, start, end, groupId, endpointId, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new OperationsRanking(reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetDouble(4)));
        return result;
    }

    private async Task<DateTime?> ReadObservedEarliestAsync(CancellationToken ct)
    {
        await using var command = await CreateCommandAsync("SELECT min(\"OccurredAt\") FROM \"ProxyRequestEvents\"", DateTime.UnixEpoch, DateTime.UnixEpoch, null, null, ct);
        var result = await command.ExecuteScalarAsync(ct);
        return result is DBNull or null ? null : (DateTime)result;
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, DateTime start, DateTime end, Guid? groupId, Guid? endpointId, CancellationToken ct, TimeSpan? bucket = null)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        command.Parameters.Add("groupId", NpgsqlDbType.Uuid).Value = groupId ?? (object)DBNull.Value;
        command.Parameters.Add("endpointId", NpgsqlDbType.Uuid).Value = endpointId ?? (object)DBNull.Value;
        if (bucket is not null) command.Parameters.AddWithValue("bucket", bucket.Value);
        return command;
    }
}

public sealed record OperationsMetrics(long Attempts, long FailedRequests, long GatewayRejected, long NetworkFailures, long ClientDisconnected, long Upstream4xx, long Upstream5xx, double? AverageLatencyMs, double? P95LatencyMs);
public sealed record OperationsSeriesBucket(DateTime From, DateTime To, long Attempts, long FailedRequests, double? AverageLatencyMs, double? P95LatencyMs);
public sealed record OperationsRanking(Guid EndpointId, string EndpointName, long Attempts, long FailedRequests, double? P95LatencyMs);
public sealed record OperationsSummary(OperationsMetrics Current, OperationsMetrics? Previous, IReadOnlyList<OperationsSeriesBucket> Series, IReadOnlyList<OperationsRanking> Rankings, DateTime? ObservedEarliestEventAt);
public sealed record OperationsStatusCount(int Status, long Attempts);
public sealed record RecentEndpoint(Guid EndpointId, string Method, string RequestPath, long Attempts, double? AverageLatencyMs, int? LatestResponseStatus, string LatestOutcome);
public sealed record OverviewGroup(Guid GroupId, string GroupName, long Attempts, int ObservedEndpoints, long SuccessfulResponses);
public sealed record OverviewOperations(OperationsMetrics CurrentMinute, OperationsMetrics PreviousMinute, OperationsMetrics Today, OperationsMetrics Yesterday, OperationsMetrics LastHour, IReadOnlyList<OperationsSeriesBucket> Traffic, IReadOnlyList<OperationsStatusCount> StatusErrors, IReadOnlyList<RecentEndpoint> RecentEndpoints, IReadOnlyList<OverviewGroup> Groups, IReadOnlyDictionary<Guid, IReadOnlyList<OperationsSeriesBucket>> GroupsHourly, DateTime? ObservedEarliestEventAt);
