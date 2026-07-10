namespace Gateway.Domain.Entities;

public class ProxyGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? BlockedIpRanges { get; set; }
    public string? AllowedIpRanges { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ProxyEndpoint> Endpoints { get; set; } = new();
}
