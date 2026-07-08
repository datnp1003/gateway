using Gateway.Domain.Models;

namespace Gateway.Domain.Interfaces;

public interface IMetricsTracker
{
    void TrackRequest(string path, int statusCode, double durationMs);
    ProxyMetrics GetMetrics();
}
