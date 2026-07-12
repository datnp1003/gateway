using Gateway.Domain.Models;

namespace Gateway.Domain.Interfaces;

public interface IMetricsTracker
{
    void TrackRequest(string path, int statusCode, double durationMs);
    ProxyMetrics GetMetrics();
    double GetRequestsPerSecond();
    double GetErrorRate();
    double GetAvgLatencyMs();
    double GetP95LatencyMs();
    int GetActiveRequests();
    List<KeyValuePair<string, int>> GetTopRoutes(int count = 5);
    List<KeyValuePair<string, int>> GetTopStatusCodes(int count = 10);
    List<MetricsSnapshot> GetHistory(int count = 60);
}

public record MetricsSnapshot(
    DateTime Timestamp,
    int TotalRequests,
    double RequestsPerSecond,
    double ErrorRate,
    double AvgLatencyMs,
    double P95LatencyMs,
    int ActiveRequests
);
