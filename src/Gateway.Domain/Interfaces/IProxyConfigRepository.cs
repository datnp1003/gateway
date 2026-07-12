using Gateway.Domain.Entities;

namespace Gateway.Domain.Interfaces;

public interface IProxyConfigRepository
{
    Task<List<ProxyGroup>> GetAllGroupsAsync(CancellationToken ct = default);
    Task<ProxyGroup?> GetGroupByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProxyGroup> CreateGroupAsync(ProxyGroup group, CancellationToken ct = default);
    Task<ProxyGroup> UpdateGroupAsync(ProxyGroup group, CancellationToken ct = default);
    Task DeleteGroupAsync(Guid id, CancellationToken ct = default);
    Task<List<ProxyEndpoint>> GetEndpointsByGroupAsync(Guid groupId, CancellationToken ct = default);
    Task<ProxyEndpoint> CreateEndpointAsync(ProxyEndpoint endpoint, CancellationToken ct = default);
    Task<ProxyEndpoint> UpdateEndpointAsync(ProxyEndpoint endpoint, CancellationToken ct = default);
    Task DeleteEndpointAsync(Guid id, CancellationToken ct = default);
    Task<List<ProxyEndpoint>> GetAllEnabledEndpointsAsync(CancellationToken ct = default);
}
