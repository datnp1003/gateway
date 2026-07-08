using System.Collections.Concurrent;
using Gateway.Domain.Interfaces;
using Gateway.Domain.Models;

namespace Gateway.Infrastructure.Services;

public class MetricsTracker : IMetricsTracker
{
    private int _totalRequests;
    private readonly ConcurrentDictionary<string, int> _statusCodes = new();
    private readonly ConcurrentDictionary<string, int> _routesHit = new();

    public void TrackRequest(string path, int statusCode, double durationMs)
    {
        Interlocked.Increment(ref _totalRequests);

        var code = statusCode.ToString();
        _statusCodes.AddOrUpdate(code, 1, (_, c) => c + 1);

        var route = GetRoutePrefix(path);
        _routesHit.AddOrUpdate(route, 1, (_, c) => c + 1);
    }

    public ProxyMetrics GetMetrics() => new(
        TotalRequests: _totalRequests,
        StatusCodes: new Dictionary<string, int>(_statusCodes),
        RoutesHit: new Dictionary<string, int>(_routesHit)
    );

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
