using System.Collections.Concurrent;
using System.Net;
using Gateway.Domain.Interfaces;
using Gateway.Infrastructure.Services;

namespace Gateway.Middleware;

/// <summary>
/// Enforces per-endpoint access policies: IP blocklist, IP allowlist, and per-IP rate-limit.
/// Skips internal management/auth/health/SPA paths.  Reads policies from the
/// in-memory EndpointPolicySnapshot (refreshed on every sync) — never the database.
/// </summary>
public class EndpointAccessPolicyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly EndpointPolicySnapshot _policySnapshot;
    private readonly ILogBuffer _logBuffer;

    // key = "{endpointId}:{clientIp}" → sliding window of request timestamps
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _rateLimitWindows = new();
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);
    private DateTime _lastPruneUtc = DateTime.UtcNow;

    // Internal paths that bypass endpoint access policy entirely.
    // /api/auth and /signin-google are the dashboard login flow: a proxy policy
    // must never be able to lock the admin out of the management surface.
    private static readonly string[] InternalPrefixes =
    [
        "/api/management",
        "/api/auth",
        "/signin-google",
        "/health",
        "/assets",
        "/favicon",
        "/@vite",
        "/src",
    ];

    public EndpointAccessPolicyMiddleware(
        RequestDelegate next,
        EndpointPolicySnapshot policySnapshot,
        ILogBuffer logBuffer)
    {
        _next = next;
        _policySnapshot = policySnapshot;
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

        // Resolve the matching endpoint from the snapshot; entries are sorted
        // longest-prefix-first, so the first hit is the most specific route.
        var endpoint = FindMatchingEndpoint(path);

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

        // 1. Blocklist check: blocked if IP matches the group OR the endpoint blocklist
        foreach (var blockedRaw in new[] { endpoint.Group?.BlockedIpRanges, endpoint.BlockedIpRanges })
        {
            if (string.IsNullOrWhiteSpace(blockedRaw)) continue;
            var blocked = ParseRanges(blockedRaw);
            if (remoteIp != null && IpMatchesAny(remoteIp, clientIp, blocked))
            {
                context.Items[ProxyAttemptProvenance.GatewayRejectedKey] = true;
                _logBuffer.AddWarning(
                    $"Access denied (blocked IP): {clientIp} → {path}",
                    path: path, method: context.Request.Method, clientIp: clientIp);
                await WriteJsonError(context, 403, "Access denied");
                return;
            }
        }

        // 2. Allowlist check: every configured allowlist (group and endpoint) must match
        foreach (var allowedRaw in new[] { endpoint.Group?.AllowedIpRanges, endpoint.AllowedIpRanges })
        {
            if (string.IsNullOrWhiteSpace(allowedRaw)) continue;
            var allowed = ParseRanges(allowedRaw);
            if (remoteIp == null || !IpMatchesAny(remoteIp, clientIp, allowed))
            {
                context.Items[ProxyAttemptProvenance.GatewayRejectedKey] = true;
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

            PruneStaleWindows(now, cutoff);

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
                context.Items[ProxyAttemptProvenance.GatewayRejectedKey] = true;
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

    private Gateway.Domain.Entities.ProxyEndpoint? FindMatchingEndpoint(PathString requestPath)
    {
        foreach (var entry in _policySnapshot.Entries)
        {
            if (requestPath.StartsWithSegments(entry.Prefix, StringComparison.OrdinalIgnoreCase))
                return entry.Endpoint;
        }
        return null;
    }

    /// <summary>
    /// Drops rate-limit windows whose entries have all aged out, so the
    /// dictionary does not grow unboundedly with one key per client IP seen.
    /// Runs at most once per PruneInterval; races only cost a redundant pass.
    /// </summary>
    private void PruneStaleWindows(DateTime now, DateTime cutoff)
    {
        if (now - _lastPruneUtc < PruneInterval) return;
        _lastPruneUtc = now;

        foreach (var (key, window) in _rateLimitWindows)
        {
            lock (window)
            {
                while (window.Count > 0 && window.Peek() < cutoff)
                    window.Dequeue();
                if (window.Count == 0)
                    _rateLimitWindows.TryRemove(key, out _);
            }
        }
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
                    prefixLen >= 0 && prefixLen <= MaxPrefixLength(network))
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
                // CIDR match within the same address family (IPv4 or IPv6)
                if (clientAddr.AddressFamily == addr.AddressFamily &&
                    IsInCidr(clientAddr, addr, prefixLen))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static int MaxPrefixLength(IPAddress network) =>
        network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;

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
