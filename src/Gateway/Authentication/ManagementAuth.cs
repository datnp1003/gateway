using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Authentication;

/// <summary>
/// Management-surface auth: Google OAuth is an identity provider only. The FE
/// starts login explicitly, the Google callback hands back a one-time code, and
/// the FE exchanges it for a Gateway-issued admin JWT sent as
/// "Authorization: Bearer" on /api/management/**.
/// The SPA shell and static assets are always served anonymously; reverse-proxy
/// routes stay auth pass-through (downstream services own their own auth).
/// </summary>
public static class ManagementAuth
{
    public const string Policy = "ManagementOnly";
    public const string DevBypassEmail = "dev@local";

    /// <summary>
    /// Cookie scheme holding the Google external identity only for the OAuth
    /// round trip (/signin-google → /api/auth/google/callback). It is signed out
    /// in the callback and is never the admin session — that is the JWT.
    /// </summary>
    public const string ExternalScheme = "External";

    public static bool IsDevBypass(IConfiguration config, IHostEnvironment env) =>
        env.IsDevelopment() && config.GetValue<bool>("Authentication:DevBypass");

    public static HashSet<string> GetAllowedEmails(IConfiguration config) =>
        (config["Authentication:AllowedEmails"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static void AddManagementAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var devBypass = IsDevBypass(config, builder.Environment);

        var jwtOptions = AdminJwtOptions.From(config, builder.Environment);
        builder.Services.AddSingleton(jwtOptions);
        builder.Services.AddSingleton<AdminTokenService>();
        builder.Services.AddSingleton<LoginCodeStore>();

        var auth = builder.Services
            // Bearer JWT is the default: failed management auth yields 401/403
            // status codes, never a redirect, and SPA/static/proxy requests are
            // unaffected by a missing or invalid Authorization header.
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = jwtOptions.Key,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
            });

        if (!devBypass)
        {
            var clientId = config["Authentication:Google:ClientId"];
            var clientSecret = config["Authentication:Google:ClientSecret"];
            var allowedEmails = GetAllowedEmails(config);

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || allowedEmails.Count == 0)
                throw new InvalidOperationException(
                    "Management auth is not configured. Set Authentication:Google:ClientId, " +
                    "Authentication:Google:ClientSecret and Authentication:AllowedEmails " +
                    "(or Authentication:DevBypass=true in Development).");

            auth.AddCookie(ExternalScheme, options =>
            {
                options.Cookie.Name = ".Gateway.External";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            });

            auth.AddGoogle(options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.SignInScheme = ExternalScheme;
                options.ClaimActions.MapJsonKey("picture", "picture");
                options.Events.OnRemoteFailure = ctx =>
                {
                    ctx.Response.Redirect("/?auth=error");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
                options.Events.OnAccessDenied = ctx =>
                {
                    ctx.Response.Redirect("/?auth=error");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(Policy, policy =>
            {
                if (devBypass)
                {
                    // Dev convenience: management API is open locally. The FE still
                    // runs the full code→JWT flow against the dev@local identity.
                    policy.RequireAssertion(_ => true);
                }
                else
                {
                    var allowedEmails = GetAllowedEmails(config);
                    policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                          .RequireAuthenticatedUser()
                          .RequireAssertion(ctx => allowedEmails.Contains(GetEmail(ctx.User) ?? ""));
                }
            });
        });
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var devBypass = IsDevBypass(app.Configuration, app.Environment);
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        // Step 1: FE asks where to send the browser. Login must only start on an
        // explicit user click, never automatically on page load.
        auth.MapGet("/challenge", (string? returnUrl) => Results.Ok(new
        {
            url = $"/api/auth/login?returnUrl={Uri.EscapeDataString(SafeLocalUrl(returnUrl))}"
        }));

        auth.MapGet("/login", (string? returnUrl, LoginCodeStore codes) =>
        {
            var target = SafeLocalUrl(returnUrl);
            if (devBypass)
            {
                var code = codes.Create(DevBypassEmail, "Dev User", null);
                return Results.Redirect(WithQuery(target, $"auth=callback&code={code}"));
            }
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = $"/api/auth/google/callback?returnUrl={Uri.EscapeDataString(target)}"
                },
                [GoogleDefaults.AuthenticationScheme]);
        });

        if (!devBypass)
        {
            // Landed on after Google's own /signin-google handler signs the external
            // cookie in. Applies the allowlist, mints the one-time code, and drops
            // the correlation cookie; the JWT itself never appears in a URL.
            auth.MapGet("/google/callback", async (string? returnUrl, HttpContext ctx, LoginCodeStore codes) =>
            {
                var target = SafeLocalUrl(returnUrl);
                var external = await ctx.AuthenticateAsync(ExternalScheme);
                if (!external.Succeeded || external.Principal is null)
                    return Results.Redirect(WithQuery(target, "auth=error"));

                var email = external.Principal.FindFirstValue(ClaimTypes.Email);
                var name = external.Principal.FindFirstValue(ClaimTypes.Name);
                var picture = external.Principal.FindFirstValue("picture");
                await ctx.SignOutAsync(ExternalScheme);

                // Deliberately no email in the URL: redirect targets end up in
                // browser history, logs and Referer headers.
                if (string.IsNullOrEmpty(email) || !GetAllowedEmails(app.Configuration).Contains(email))
                    return Results.Redirect(WithQuery(target, "auth=denied"));

                var code = codes.Create(email, name, picture);
                return Results.Redirect(WithQuery(target, $"auth=callback&code={code}"));
            });
        }

        auth.MapPost("/exchange", (ExchangeRequest req, LoginCodeStore codes, AdminTokenService tokens) =>
        {
            if (string.IsNullOrEmpty(req.Code) || !codes.TryConsume(req.Code, out var login))
                return Results.Json(new { error = "Invalid, expired or already used code." },
                    statusCode: StatusCodes.Status401Unauthorized);

            var (accessToken, expiresAt) = tokens.Issue(login.Email, login.Name, login.Picture);
            return Results.Ok(new
            {
                accessToken,
                expiresAt,
                user = new { email = login.Email, name = login.Name, picture = login.Picture }
            });
        });

        auth.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
                return Results.Json(new { authenticated = false }, statusCode: StatusCodes.Status401Unauthorized);

            var expiresAt = long.TryParse(user.FindFirstValue("exp"), out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp)
                : (DateTimeOffset?)null;
            return Results.Ok(new
            {
                authenticated = true,
                user = new
                {
                    email = GetEmail(user),
                    name = user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name),
                    picture = user.FindFirstValue("picture")
                },
                expiresAt
            });
        });

        // Stateless for MVP: the FE discards the token; the short TTL bounds any
        // residual validity. No server-side revocation.
        auth.MapPost("/logout", () => Results.NoContent());
    }

    private static string? GetEmail(ClaimsPrincipal user) =>
        user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email);

    /// <summary>
    /// One-time login codes may only land on the SPA dashboard shell. The FE
    /// always sends returnUrl=/, so this is an exact whitelist — anything else
    /// (/api, /health, assets, proxy routes, //host or /\host variants,
    /// absolute URLs) falls back to the dashboard root.
    /// </summary>
    private static string SafeLocalUrl(string? url) =>
        url == "/" ? url : "/";

    private static string WithQuery(string path, string query) =>
        path.Contains('?') ? $"{path}&{query}" : $"{path}?{query}";
}

public sealed record ExchangeRequest(string? Code);
