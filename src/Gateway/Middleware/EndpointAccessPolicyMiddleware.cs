using System.Collections.Concurrent;
using System.Net;
using Gateway.Domain.Interfaces;

namespace Gateway.Middleware;

/// <summary>
/// Enforces per-endpoint access policies: IP blocklist, IP allowlist, and per-IP rate-limit.
/// Skips internal management/health/SPA paths.  No new packages – only stdlib IPAddress.
/// </summary>
public class EndpointAccessPolicyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogBuffer _logBuffer;

    // key = "{endpointId}:{clientIp}" → sliding window of request timestamps
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _rateLimitWindows = new();

    // Internal paths that bypass endpoint access policy entirely
    private static readonly string[] InternalPrefixes =
    [
        "/api/management",
        "/health",
        "/assets",
        "/favicon",
        "/@vite",
        "/src",
    ];

    public EndpointAccessPolicyMiddleware(
        RequestDelegate next,
        IServiceScopeFactory scopeFactory,
        ILogBuffer logBuffer)
    {
        _next = next;
        _scopeFactory = scopeFactory;
        _logBuffer = logBuffer;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        // Skip root path and all internal prefixes
        if (path == "/" || IsInternalPath(path))
        {
            await _next(context);
            return;
        }

        // Resolve the matching endpoint
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProxyConfigRepository>();

        var endpoints = await repo.GetAllEnabledEndpointsAsync();
        var endpoint = FindMatchingEndpoint(endpoints, path);

        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        // Determine client IP
        var remoteIp = context.Connection.RemoteIpAddress;
        var clientIp = remoteIp?.ToString() ?? "unknown";

        // Normalize IPv6 loopback to make blocklist/allowlist intuitive
        if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();

        // 1. Blocklist check
        if (!string.IsNullOrWhiteSpace(endpoint.BlockedIpRanges))
        {
            var blocked = ParseRanges(endpoint.BlockedIpRanges);
            if (remoteIp != null && IpMatchesAny(remoteIp, clientIp, blocked))
            {
                _logBuffer.AddWarning(
                    $"Access denied (blocked IP): {clientIp} → {path}",
                    path: path, method: context.Request.Method, clientIp: clientIp);
                await WriteJsonError(context, 403, "Access denied");
                return;
            }
        }

        // 2. Allowlist check (non-empty allowlist = whitelist mode)
        if (!string.IsNullOrWhiteSpace(endpoint.AllowedIpRanges))
        {
            var allowed = ParseRanges(endpoint.AllowedIpRanges);
            if (remoteIp == null || !IpMatchesAny(remoteIp, clientIp, allowed))
            {
                _logBuffer.AddWarning(
                    $"Access denied (not in allowlist): {clientIp} → {path}",
                    path: path, method: context.Request.Method, clientIp: clientIp);
                await WriteJsonError(context, 403, "Access denied");
                return;
            }
        }

        // 3. Per-IP rate-limit
        if (endpoint.RateLimitPerMinute is > 0)
        {
            var key = $"{endpoint.Id}:{clientIp}";
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-1);

            var window = _rateLimitWindows.GetOrAdd(key, _ => new Queue<DateTime>());
            bool rateLimitExceeded;
            lock (window)
            {
                // Remove entries older than 1 minute
                while (window.Count > 0 && window.Peek() < cutoff)
                    window.Dequeue();

                if (window.Count >= endpoint.RateLimitPerMinute.Value)
                {
                    rateLimitExceeded = true;
                }
                else
                {
                    window.Enqueue(now);
                    rateLimitExceeded = false;
                }
            }

            if (rateLimitExceeded)
            {
                _logBuffer.AddWarning(
                    $"Rate limit exceeded ({endpoint.RateLimitPerMinute}/min): {clientIp} → {path}",
                    path: path, method: context.Request.Method, clientIp: clientIp);
                context.Response.StatusCode = 429;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                return;
            }
        }

        await _next(context);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsInternalPath(PathString path)
    {
        foreach (var prefix in InternalPrefixes)
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static Gateway.Domain.Entities.ProxyEndpoint? FindMatchingEndpoint(
        IEnumerable<Gateway.Domain.Entities.ProxyEndpoint> endpoints, PathString requestPath)
    {
        foreach (var ep in endpoints)
        {
            var groupPath = ep.Group?.Path?.TrimEnd('/') ?? string.Empty;
            // Strip catch-all suffix from endpoint path pattern to get a prefix
            var epPattern = ep.PathPattern ?? string.Empty;
            var catchAllIdx = epPattern.IndexOf("{**", StringComparison.Ordinal);
            var epPrefix = catchAllIdx >= 0
                ? epPattern[..catchAllIdx].TrimEnd('/')
                : epPattern.TrimEnd('/');

            var fullPrefix = string.IsNullOrEmpty(epPrefix)
                ? $"/{groupPath.TrimStart('/')}"
                : $"/{groupPath.TrimStart('/')}{epPrefix}";

            // Normalize double slashes
            fullPrefix = "/" + fullPrefix.TrimStart('/');

            if (requestPath.StartsWithSegments(fullPrefix, StringComparison.OrdinalIgnoreCase))
                return ep;
        }
        return null;
    }

    /// <summary>
    /// Parses a newline/comma-separated list of IPs and CIDR ranges.
    /// Invalid entries are ignored (with a warning logged).
    /// Returns list of (IPAddress network, int prefixLen) tuples; exact IPs use prefixLen=-1.
    /// </summary>
    private List<(IPAddress Addr, int PrefixLen)> ParseRanges(string raw)
    {
        var result = new List<(IPAddress, int)>();
        var entries = raw.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var s = entry.Trim();
            if (string.IsNullOrEmpty(s)) continue;

            var slashIdx = s.IndexOf('/');
            if (slashIdx >= 0)
            {
                // CIDR
                var addrPart = s[..slashIdx];
                var lenPart = s[(slashIdx + 1)..];
                if (IPAddress.TryParse(addrPart, out var network) &&
                    int.TryParse(lenPart, out var prefixLen) &&
                    prefixLen >= 0 && prefixLen <= 32)
                {
                    result.Add((network, prefixLen));
                }
                else
                {
                    _logBuffer.AddWarning($"EndpointAccessPolicy: invalid CIDR entry ignored: {s}");
                }
            }
            else
            {
                // Exact IP
                if (IPAddress.TryParse(s, out var addr))
                    result.Add((addr, -1));
                else
                    _logBuffer.AddWarning($"EndpointAccessPolicy: invalid IP entry ignored: {s}");
            }
        }
        return result;
    }

    private static bool IpMatchesAny(IPAddress clientAddr, string clientIpStr,
        IEnumerable<(IPAddress Addr, int PrefixLen)> ranges)
    {
        foreach (var (addr, prefixLen) in ranges)
        {
            if (prefixLen == -1)
            {
                // Exact match – also handle ::1 loopback
                if (clientAddr.Equals(addr)) return true;
                // Allow matching "::1" against "127.0.0.1" entries and vice-versa via string
                if (clientIpStr == "::1" && addr.ToString() == "127.0.0.1") return true;
                if (clientIpStr == "127.0.0.1" && addr.ToString() == "::1") return true;
            }
            else
            {
                // IPv4 CIDR match
                if (clientAddr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    if (IsInCidr(clientAddr, addr, prefixLen)) return true;
                }
            }
        }
        return false;
    }

    private static bool IsInCidr(IPAddress clientAddr, IPAddress network, int prefixLen)
    {
        var clientBytes = clientAddr.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        if (clientBytes.Length != networkBytes.Length) return false;

        var fullBytes = prefixLen / 8;
        var remainBits = prefixLen % 8;

        for (var i = 0; i < fullBytes; i++)
            if (clientBytes[i] != networkBytes[i]) return false;

        if (remainBits > 0 && fullBytes < clientBytes.Length)
        {
            var mask = (byte)(0xFF << (8 - remainBits));
            if ((clientBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask)) return false;
        }

        return true;
    }

    private static async Task WriteJsonError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}
