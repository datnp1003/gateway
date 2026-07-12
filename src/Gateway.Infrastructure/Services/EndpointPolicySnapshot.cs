using Gateway.Domain.Entities;

namespace Gateway.Infrastructure.Services;

/// <summary>
/// Immutable in-memory view of the enabled endpoints and their access policies.
/// Rebuilt atomically on every YARP sync (startup + each CRUD change) so the
/// per-request access-policy middleware never queries the database. Entries are
/// ordered longest-prefix-first to mirror YARP's most-specific-route-wins.
/// </summary>
public sealed class EndpointPolicySnapshot
{
    public sealed record Entry(string Prefix, ProxyEndpoint Endpoint);

    private volatile IReadOnlyList<Entry> _entries = Array.Empty<Entry>();

    public IReadOnlyList<Entry> Entries => _entries;

    public void Update(IEnumerable<ProxyEndpoint> endpoints) =>
        _entries = endpoints
            .Select(e => new Entry(BuildMatchPrefix(e), e))
            .OrderByDescending(e => e.Prefix.Length)
            .ToList();

    /// <summary>Public path prefix served by an endpoint: /{group.Path}{PathPattern minus catch-all}.</summary>
    private static string BuildMatchPrefix(ProxyEndpoint endpoint)
    {
        var groupPath = endpoint.Group?.Path?.Trim('/') ?? string.Empty;

        var pattern = endpoint.PathPattern ?? string.Empty;
        var catchAllIdx = pattern.IndexOf("{**", StringComparison.Ordinal);
        var patternPrefix = catchAllIdx >= 0
            ? pattern[..catchAllIdx].TrimEnd('/')
            : pattern.TrimEnd('/');

        var fullPrefix = string.IsNullOrEmpty(patternPrefix)
            ? $"/{groupPath}"
            : $"/{groupPath}{patternPrefix}";
        return "/" + fullPrefix.TrimStart('/');
    }
}
