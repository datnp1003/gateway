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
    double? DurationMs,
    string? Method = null,
    string? ClientIp = null,
    string? Destination = null,
    string? RequestId = null
);

public record ProxyMetrics(
    int TotalRequests,
    Dictionary<string, int> StatusCodes,
    Dictionary<string, int> RoutesHit,
    double RequestsPerSecond = 0,
    double ErrorRate = 0,
    double AvgLatencyMs = 0,
    int ActiveRequests = 0,
    List<KeyValuePair<string, int>> TopRoutes = null!,
    List<KeyValuePair<string, int>> TopStatusCodes = null!
);
