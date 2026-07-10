using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Gateway.Authentication;

public sealed record LoginCodeEntry(string Email, string? Name, string? Picture, DateTimeOffset ExpiresAt);

/// <summary>
/// In-memory one-time login codes bridging the OAuth callback redirect and the
/// FE token exchange, so the admin JWT itself never appears in a URL.
/// Single-instance only by design (see plan's "ponytail" notes); codes expire
/// after a short TTL and are deleted on first use.
/// </summary>
public sealed class LoginCodeStore
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, LoginCodeEntry> _codes = new();

    public string Create(string email, string? name, string? picture)
    {
        RemoveExpired();
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _codes[code] = new LoginCodeEntry(email, name, picture, DateTimeOffset.UtcNow + Ttl);
        return code;
    }

    public bool TryConsume(string code, out LoginCodeEntry entry)
    {
        RemoveExpired();
        if (_codes.TryRemove(code, out var found) && found.ExpiresAt > DateTimeOffset.UtcNow)
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _codes)
            if (kvp.Value.ExpiresAt <= now)
                _codes.TryRemove(kvp.Key, out _);
    }
}
