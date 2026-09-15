namespace Gateway.Domain.Entities;

public class ProxyEndpoint
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PathPattern { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string? RemovePrefix { get; set; }
    // Legacy storage only; never an authorization policy or management contract.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RequiresAuth { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int? RateLimitPerMinute { get; set; }
    public string? BlockedIpRanges { get; set; }
    public string? AllowedIpRanges { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ProxyGroup Group { get; set; } = null!;
}
