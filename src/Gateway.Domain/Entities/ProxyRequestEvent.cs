namespace Gateway.Domain.Entities;

public class ProxyRequestEvent
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public double? DurationMs { get; set; }
    public string Method { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public Guid EndpointId { get; set; }
    public Guid GroupId { get; set; }
    public string EndpointName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string ConfiguredDestination { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public int? ResponseStatus { get; set; }
    public string? ClientIp { get; set; }
}
