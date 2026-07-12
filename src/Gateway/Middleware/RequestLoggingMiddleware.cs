using Gateway.Domain.Models;
using Gateway.Domain.Interfaces;

namespace Gateway.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly ILogBuffer _logBuffer;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, ILogBuffer logBuffer)
    {
        _next = next;
        _logger = logger;
        _logBuffer = logBuffer;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start = DateTime.UtcNow;
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;
        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var requestId = context.TraceIdentifier;
        var isInternal = IsInternalRequest(context.Request.Path);

        try
        {
            await _next(context);
            var elapsed = DateTime.UtcNow - start;
            var statusCode = context.Response.StatusCode;

            if (!isInternal)
            {
                _logger.LogInformation(
                    "[{Method}] {Path} -> {StatusCode} ({Elapsed}ms)",
                    method,
                    path,
                    statusCode,
                    elapsed.TotalMilliseconds.ToString("F1"));

                // Feed log buffer for dashboard
                _logBuffer.AddInfo(
                    message: $"{method} {path}",
                    path: path,
                    statusCode: statusCode,
                    durationMs: elapsed.TotalMilliseconds,
                    method: method,
                    clientIp: clientIp,
                    destination: context.Request.Host.Value,
                    requestId: requestId);

                // Feed metrics to dashboard
                var metrics = context.RequestServices.GetService<IMetricsTracker>();
                metrics?.TrackRequest(
                    context.Request.Path,
                    statusCode,
                    elapsed.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            if (!isInternal)
            {
                _logBuffer.AddError(
                    message: $"{method} {path} — {ex.GetType().Name}: {ex.Message}",
                    path: path,
                    method: method,
                    clientIp: clientIp,
                    requestId: requestId);
            }
            throw;
        }
    }

    private static bool IsInternalRequest(PathString path)
    {
        return path.StartsWithSegments("/api/management")
            || path.StartsWithSegments("/health")
            || path.StartsWithSegments("/assets")
            || path.StartsWithSegments("/favicon.svg")
            || path.StartsWithSegments("/@vite")
            || path.StartsWithSegments("/src")
            || path == "/";
    }
}
