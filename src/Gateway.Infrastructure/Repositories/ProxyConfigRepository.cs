using Microsoft.EntityFrameworkCore;
using Gateway.Domain.Entities;
using Gateway.Domain.Interfaces;
using Gateway.Infrastructure.Data;

namespace Gateway.Infrastructure.Repositories;

public class ProxyConfigRepository : IProxyConfigRepository
{
    private readonly GatewayDbContext _db;

    public ProxyConfigRepository(GatewayDbContext db) => _db = db;

    public async Task<List<ProxyGroup>> GetAllGroupsAsync(CancellationToken ct = default) =>
        await _db.Groups.Include(g => g.Endpoints).ToListAsync(ct);

    public async Task<ProxyGroup?> GetGroupByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Groups.Include(g => g.Endpoints).FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<ProxyGroup> CreateGroupAsync(ProxyGroup group, CancellationToken ct = default)
    {
        group.Id = Guid.NewGuid();
        group.CreatedAt = DateTime.UtcNow;
        group.UpdatedAt = DateTime.UtcNow;
        _db.Groups.Add(group);
        await _db.SaveChangesAsync(ct);
        return group;
    }

    public async Task<ProxyGroup> UpdateGroupAsync(ProxyGroup group, CancellationToken ct = default)
    {
        var existing = await _db.Groups.FindAsync([group.Id], ct)
            ?? throw new KeyNotFoundException($"Group {group.Id} not found");
        existing.Name = group.Name;
        existing.Path = group.Path;
        existing.Description = group.Description;
        existing.IsEnabled = group.IsEnabled;
        existing.BlockedIpRanges = group.BlockedIpRanges;
        existing.AllowedIpRanges = group.AllowedIpRanges;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken ct = default)
    {
        var group = await _db.Groups.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Group {id} not found");
        _db.Groups.Remove(group);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<ProxyEndpoint>> GetEndpointsByGroupAsync(Guid groupId, CancellationToken ct = default) =>
        await _db.Endpoints.Where(e => e.GroupId == groupId).ToListAsync(ct);

    public async Task<ProxyEndpoint> CreateEndpointAsync(ProxyEndpoint endpoint, CancellationToken ct = default)
    {
        endpoint.Id = Guid.NewGuid();
        endpoint.CreatedAt = DateTime.UtcNow;
        endpoint.UpdatedAt = DateTime.UtcNow;
        _db.Endpoints.Add(endpoint);
        await _db.SaveChangesAsync(ct);
        return endpoint;
    }

    public async Task<ProxyEndpoint> UpdateEndpointAsync(ProxyEndpoint endpoint, CancellationToken ct = default)
    {
        var existing = await _db.Endpoints.FindAsync([endpoint.Id], ct)
            ?? throw new KeyNotFoundException($"Endpoint {endpoint.Id} not found");
        existing.Name = endpoint.Name;
        existing.PathPattern = endpoint.PathPattern;
        existing.Destination = endpoint.Destination;
        existing.RemovePrefix = endpoint.RemovePrefix;
        existing.RequiresAuth = endpoint.RequiresAuth;
        existing.IsEnabled = endpoint.IsEnabled;
        existing.RateLimitPerMinute = endpoint.RateLimitPerMinute;
        existing.BlockedIpRanges = endpoint.BlockedIpRanges;
        existing.AllowedIpRanges = endpoint.AllowedIpRanges;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteEndpointAsync(Guid id, CancellationToken ct = default)
    {
        var ep = await _db.Endpoints.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Endpoint {id} not found");
        _db.Endpoints.Remove(ep);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<ProxyEndpoint>> GetAllEnabledEndpointsAsync(CancellationToken ct = default) =>
        await _db.Endpoints
            .Include(e => e.Group)
            .Where(e => e.IsEnabled && e.Group.IsEnabled)
            .ToListAsync(ct);
}
