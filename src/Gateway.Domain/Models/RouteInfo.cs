namespace Gateway.Domain.Models;

public record RouteInfo(
    string RouteId,
    string ClusterId,
    string MatchPath,
    string? AuthorizationPolicy,
    List<string> Transforms
);
