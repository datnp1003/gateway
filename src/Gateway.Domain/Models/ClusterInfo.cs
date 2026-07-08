namespace Gateway.Domain.Models;

public record ClusterInfo(
    string ClusterId,
    List<DestinationInfo> Destinations
);

public record DestinationInfo(
    string Name,
    string Address,
    string? HealthStatus
);
