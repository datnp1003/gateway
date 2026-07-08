using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using OpenTelemetry.Trace;
using Gateway.Endpoints;
using Gateway.Domain.Interfaces;
using Gateway.Infrastructure.Data;
using Gateway.Infrastructure.Repositories;
using Gateway.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/gateway-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // ─── SQLite + EF Core ───
    builder.Services.AddDbContext<GatewayDbContext>(options =>
        options.UseSqlite("Data Source=gateway.db"));

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

    // ─── Dashboard services ───
    builder.Services.AddSingleton<ILogBuffer, LogBuffer>();
    builder.Services.AddSingleton<IMetricsTracker, MetricsTracker>();

    // ─── JWT Authentication ───
    var jwtSecret = builder.Configuration["Jwt:Secret"]!;
    var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
    var jwtAudience = builder.Configuration["Jwt:Audience"]!;

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Authenticated", policy =>
            policy.RequireAuthenticatedUser());
    });

    // ─── Rate Limiting ───
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddFixedWindowLimiter("fixed", config =>
        {
            config.PermitLimit = 100;
            config.Window = TimeSpan.FromMinutes(1);
            config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            config.QueueLimit = 10;
        });

        options.AddFixedWindowLimiter("auth", config =>
        {
            config.PermitLimit = 20;
            config.Window = TimeSpan.FromMinutes(1);
            config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            config.QueueLimit = 5;
        });
    });

    // ─── CORS ───
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
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
        db.Database.EnsureCreated();
        await SeedData.SeedFromAppSettingsAsync(db, builder.Configuration);

        var syncer = scope.ServiceProvider.GetRequiredService<IYarpConfigSyncService>();
        await syncer.SyncFromDatabaseAsync();
    }

    // ─── Middleware Pipeline (order matters!) ───
    app.UseMiddleware<Gateway.Middleware.ExceptionHandlingMiddleware>();
    app.UseMiddleware<Gateway.Middleware.RequestLoggingMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors("AllowFrontend");
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // Health check endpoint (no auth required)
    app.MapHealthChecks("/health");

    // Management API
    app.MapManagementApi();

    // Serve React SPA static files (only for non-API paths)
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api")
                       && !ctx.Request.Path.StartsWithSegments("/health"),
        spa =>
        {
            spa.UseDefaultFiles();
            spa.UseStaticFiles();
        });

    // YARP reverse proxy handles all other routes
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
