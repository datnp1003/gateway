using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Authentication;

/// <summary>
/// Signing configuration for the Gateway-issued admin JWT (management surface only).
/// Resolved eagerly at startup so a missing/weak secret fails the app closed
/// instead of failing the first login.
/// </summary>
public sealed record AdminJwtOptions(
    string Issuer, string Audience, SymmetricSecurityKey Key, TimeSpan Lifetime)
{
    // Only ever used when ASPNETCORE_ENVIRONMENT=Development and no secret is
    // configured; kept in code (not appsettings.Development.json) so a
    // user-configured secret is never overridden by config layering.
    private const string DevFallbackSecret = "local-dev-minimum-32-char-secret-change-me";

    public static AdminJwtOptions From(IConfiguration config, IHostEnvironment env)
    {
        var jwt = config.GetSection("Authentication:Jwt");
        var secret = jwt["Secret"];
        if (string.IsNullOrWhiteSpace(secret) && env.IsDevelopment())
            secret = DevFallbackSecret;
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException(
                "Authentication:Jwt:Secret must be configured with at least 32 bytes " +
                "(env Authentication__Jwt__Secret) outside Development.");

        var minutes = Math.Clamp(jwt.GetValue<int?>("AccessTokenMinutes") ?? 60, 1, 24 * 60);
        return new AdminJwtOptions(
            jwt["Issuer"] ?? "Gateway.Admin",
            jwt["Audience"] ?? "Gateway.Dashboard",
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            TimeSpan.FromMinutes(minutes));
    }
}

/// <summary>
/// Issues the short-lived admin JWT the dashboard sends as
/// "Authorization: Bearer" on /api/management/**. No refresh/revocation for MVP;
/// the token TTL is the session lifetime.
/// </summary>
public sealed class AdminTokenService(AdminJwtOptions options)
{
    public (string AccessToken, DateTimeOffset ExpiresAt) Issue(string email, string? name, string? picture)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + options.Lifetime;

        var claims = new Dictionary<string, object>
        {
            ["sub"] = email,
            ["email"] = email,
        };
        if (!string.IsNullOrEmpty(name)) claims["name"] = name;
        if (!string.IsNullOrEmpty(picture)) claims["picture"] = picture;

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(options.Key, SecurityAlgorithms.HmacSha256),
        });
        return (token, expiresAt);
    }
}
