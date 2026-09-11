using System.Net;
using Serilog;
using Serilog.Sinks.Elasticsearch;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using OpenTelemetry.Trace;
using Gateway.Authentication;
using Gateway.Endpoints;
using Gateway.Domain.Interfaces;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Repositories;
using Gateway.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    var esConfig = builder.Configuration.GetSection("Elasticsearch");
    var esEnabled = esConfig.GetValue<bool>("Enabled");
    var esUrl = esConfig.GetValue<string>("Url") ?? "http://localhost:9200";
    var esIndexFormat = esConfig.GetValue<string>("IndexFormat") ?? "gateway-logs-{0:yyyy.MM}";

    var loggerConfig = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Yarp.ReverseProxy", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.Console()
        .WriteTo.File("logs/gateway-.log", rollingInterval: RollingInterval.Day);

    if (esEnabled)
    {
        loggerConfig.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUrl))
        {
            AutoRegisterTemplate = true,
            IndexFormat = esIndexFormat,
            FailureCallback = (logEvent, ex) =>
                Console.Error.WriteLine($"Elasticsearch sink error: {ex?.Message}")
        });
    }

    Log.Logger = loggerConfig.CreateLogger();

    // ─── PostgreSQL + EF Core ───
    var gatewayConnectionString = builder.Configuration.GetConnectionString("Gateway")
        ?? throw new InvalidOperationException("ConnectionStrings:Gateway must configure PostgreSQL.");
    builder.Services.AddDbContext<GatewayDbContext>(options =>
        options.UseNpgsql(gatewayConnectionString));

    // ─── YARP with InMemoryConfig (dynamic, no appsettings dependency) ───
    var yarpConfig = new InMemoryConfigProvider(
        new List<RouteConfig>(),
        new List<ClusterConfig>());
    builder.Services.AddSingleton(yarpConfig);
    builder.Services.AddReverseProxy()
        .LoadFromMemory(
            yarpConfig.GetConfig().Routes,
            yarpConfig.GetConfig().Clusters);

    // ─── Repository + Config Sync ───
    builder.Services.AddScoped<IProxyConfigRepository, ProxyConfigRepository>();
    builder.Services.AddScoped<IYarpConfigSyncService, YarpConfigSyncService>();
    // Sync lock must outlive scopes: serializes syncs across concurrent requests.
    builder.Services.AddSingleton<YarpConfigSyncCoordinator>();
    // Access-policy data snapshot: written by sync, read lock-free per request.
    builder.Services.AddSingleton<EndpointPolicySnapshot>();

    // ─── Dashboard services ───
    builder.Services.AddSingleton<ILogBuffer, LogBuffer>();
    builder.Services.AddSingleton<IMetricsTracker, MetricsTracker>();
    builder.Services.AddScoped<ProxyOperationsQueryService>();
    builder.Services.AddSingleton<ProxyAttemptWriter>();
    builder.Services.AddSingleton<IProxyAttemptSink>(sp => sp.GetRequiredService<ProxyAttemptWriter>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ProxyAttemptWriter>());
    builder.Services.AddHostedService<ProxyEventRetentionService>();

    // ─── Auth ───
    // Proxy routes stay pass-through: downstream services own auth/authorization,
    // Authorization/Cookie headers are forwarded to destinations untouched.
    // Only /api/management/** requires auth: a Gateway-issued admin JWT
    // (Google OAuth → one-time code → bearer token). The SPA shell itself is
    // always served anonymously; the FE owns login/denied states.
    builder.AddManagementAuth();

    // ─── Rate Limiting (per-client IP) ───
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, ct) =>
        {
            context.HttpContext.Items[Gateway.Middleware.ProxyAttemptProvenance.GatewayRejectedKey] = true;
            var logBuffer = context.HttpContext.RequestServices.GetRequiredService<ILogBuffer>();
            logBuffer.AddWarning(
                message: $"Rate limit exceeded: {context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
                path: context.HttpContext.Request.Path,
                method: context.HttpContext.Request.Method,
                clientIp: context.HttpContext.Connection.RemoteIpAddress?.ToString());
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Too many requests. Try again later.", retryAfter = context.HttpContext.Response.Headers.RetryAfter.ToString() }, ct);
        };

        // Management API: 100 req/min per IP
        options.AddPolicy("management", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10
                }));

        // Auth endpoints: 20 req/min per IP
        options.AddPolicy("auth", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 5
                }));

        // Global proxy: 500 req/min per IP
        options.AddPolicy("proxy", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 500,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 50
                }));
    });

    // ─── Gateway management CORS ───
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("GatewayManagement", policy =>
        {
            policy.WithOrigins(
                    "http://localhost:3000",
                    "http://localhost:3001")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // ─── Health Checks ───
    builder.Services.AddHealthChecks();

    // ─── OpenTelemetry ───
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation();
            tracing.AddConsoleExporter();
        });

    // ─── JSON: ignore circular references (Group ↔ Endpoints) ───
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

    var app = builder.Build();

    // ─── Startup: ensure DB + sync routes ───
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        await db.Database.MigrateAsync();
        await SeedData.SeedFromAppSettingsAsync(db, builder.Configuration);

        var syncer = scope.ServiceProvider.GetRequiredService<IYarpConfigSyncService>();
        await syncer.SyncFromDatabaseAsync();
    }

    // ─── Middleware Pipeline (order matters!) ───

    // Forwarded headers first, so IP-based policies and rate limiting see the
    // real client behind a reverse proxy. Only explicitly configured hops are
    // trusted; with no configuration X-Forwarded-For is ignored entirely.
    var trustedProxies = builder.Configuration
        .GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>() ?? [];
    var trustedNetworks = builder.Configuration
        .GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>() ?? [];
    if (trustedProxies.Length > 0 || trustedNetworks.Length > 0)
    {
        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        foreach (var proxy in trustedProxies)
        {
            if (!IPAddress.TryParse(proxy, out var proxyIp))
                throw new InvalidOperationException(
                    $"ForwardedHeaders:TrustedProxies contains an invalid IP address: '{proxy}'");
            forwardedOptions.KnownProxies.Add(proxyIp);
        }
        foreach (var network in trustedNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out var net))
                throw new InvalidOperationException(
                    $"ForwardedHeaders:TrustedNetworks contains an invalid CIDR range: '{network}'");
            forwardedOptions.KnownIPNetworks.Add(net);
        }
        app.UseForwardedHeaders(forwardedOptions);
    }

    app.UseMiddleware<Gateway.Middleware.ExceptionHandlingMiddleware>();
    app.UseRouting();
    app.UseMiddleware<Gateway.Middleware.ProxyAttemptCaptureMiddleware>();
    app.UseMiddleware<Gateway.Middleware.RequestLoggingMiddleware>();
    app.UseMiddleware<Gateway.Middleware.EndpointAccessPolicyMiddleware>();

    // Only endpoint metadata opts into Gateway-managed CORS. Proxy routes keep
    // downstream CORS behavior and forward preflight requests unchanged.
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // Health check endpoint (no auth required)
    app.MapHealthChecks("/health");

    // Auth endpoints (challenge/login/exchange/me/logout)
    app.MapAuthEndpoints();

    // Management API (requires admin JWT via ManagementOnly policy)
    app.MapManagementApi();

    // Serve React SPA static files (only for non-API paths), always anonymously —
    // authentication state is rendered by the FE, not gated by the server.
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api")
                       && !ctx.Request.Path.StartsWithSegments("/health"),
        spa =>
        {
            spa.UseDefaultFiles();
            spa.UseStaticFiles();
        });

    // YARP reverse proxy handles all other routes; every generated route sets
    // RouteConfig.RateLimiterPolicy = "proxy" (see YarpConfigSyncService).
    app.MapReverseProxy();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Gateway terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// For integration tests
public partial class Program { }
