using System.Collections.Concurrent;
using Gateway.Domain.Interfaces;
using Gateway.Domain.Models;

namespace Gateway.Infrastructure.Services;

public class MetricsTracker : IMetricsTracker
{
    private int _totalRequests;
    private int _activeRequests;
    private readonly ConcurrentDictionary<string, int> _statusCodes = new();
    private readonly ConcurrentDictionary<string, int> _routesHit = new();
    private readonly ConcurrentQueue<(DateTime Timestamp, double DurationMs)> _recentRequests = new();
    private readonly ConcurrentQueue<MetricsSnapshot> _history = new();
    private readonly object _lock = new();

    public void TrackRequest(string path, int statusCode, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _activeRequests);

        var code = statusCode.ToString();
        _statusCodes.AddOrUpdate(code, 1, (_, c) => c + 1);

        var route = GetRoutePrefix(path);
        _routesHit.AddOrUpdate(route, 1, (_, c) => c + 1);

        // Sliding window for RPS calculation (keep last 60s)
        var now = DateTime.UtcNow;
        _recentRequests.Enqueue((now, durationMs));
        PruneOldEntries();

        // Record a snapshot every ~1 second
        lock (_lock)
        {
            if (_history.IsEmpty || (now - _history.Last().Timestamp).TotalSeconds >= 1)
            {
                _history.Enqueue(new MetricsSnapshot(
                    now,
                    _totalRequests,
                    GetRequestsPerSecond(),
                    GetErrorRate(),
                    GetAvgLatencyMs(),
                    _activeRequests
                ));
                while (_history.Count > 300)
                    _history.TryDequeue(out _);
            }
        }

        Interlocked.Decrement(ref _activeRequests);
    }

    private void PruneOldEntries()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        while (_recentRequests.TryPeek(out var entry) && entry.Timestamp < cutoff)
            _recentRequests.TryDequeue(out _);
    }

    public double GetRequestsPerSecond()
    {
        PruneOldEntries();
        var count = _recentRequests.Count;
        if (count == 0) return 0;

        var oldest = _recentRequests.TryPeek(out var first) ? first.Timestamp : DateTime.UtcNow;
        var span = (DateTime.UtcNow - oldest).TotalSeconds;
        return span > 0 ? count / span : count;
    }

    public double GetErrorRate()
    {
        if (_totalRequests == 0) return 0;
        var errorCount = _statusCodes
            .Where(kv => kv.Key.StartsWith('5'))
            .Sum(kv => kv.Value);
        return (double)errorCount / _totalRequests * 100;
    }

    public double GetAvgLatencyMs()
    {
        var recent = _recentRequests.ToArray();
        if (recent.Length == 0) return 0;
        return recent.Average(r => r.DurationMs);
    }

    public int GetActiveRequests() => _activeRequests;

    public List<KeyValuePair<string, int>> GetTopRoutes(int count = 5) =>
        _routesHit
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .ToList();

    public List<KeyValuePair<string, int>> GetTopStatusCodes(int count = 10) =>
        _statusCodes
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .ToList();

    public ProxyMetrics GetMetrics() => new(
        TotalRequests: _totalRequests,
        StatusCodes: new Dictionary<string, int>(_statusCodes),
        RoutesHit: new Dictionary<string, int>(_routesHit),
        RequestsPerSecond: GetRequestsPerSecond(),
        ErrorRate: GetErrorRate(),
        AvgLatencyMs: GetAvgLatencyMs(),
        ActiveRequests: GetActiveRequests(),
        TopRoutes: GetTopRoutes(),
        TopStatusCodes: GetTopStatusCodes()
    );

    public List<MetricsSnapshot> GetHistory(int count = 60)
    {
        var snapshots = _history.ToArray();
        return snapshots.Reverse().Take(count).Reverse().ToList();
    }

    private static string GetRoutePrefix(string path) =>
        path switch
        {
            "/health" => "health",
            _ when path.StartsWith("/api/auth") => "auth",
            _ when path.StartsWith("/api/learning") => "learning",
            _ when path.StartsWith("/api/ai") => "ai",
            _ when path.StartsWith("/api/writing") => "writing",
            _ when path.StartsWith("/api/speaking") => "speaking",
            _ when path.StartsWith("/api/gamification") => "gamification",
            _ => "other"
        };
}
