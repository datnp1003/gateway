namespace Gateway.Domain.Models;

public record DashboardData(
    DateTime StartedAt,
    TimeSpan Uptime,
    int TotalRequests,
    int ActiveConnections,
    List<RouteInfo> Routes,
    List<ClusterInfo> Clusters,
    List<LogEntry> RecentLogs
);

public record LogEntry(
    DateTime Timestamp,
    string Level,
    string Message,
    string? Path,
    int? StatusCode,
    double? DurationMs
);

public record ProxyMetrics(
    int TotalRequests,
    Dictionary<string, int> StatusCodes,
    Dictionary<string, int> RoutesHit
);
