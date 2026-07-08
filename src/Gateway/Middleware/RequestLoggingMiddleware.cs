using Gateway.Domain.Models;
using Gateway.Domain.Interfaces;

namespace Gateway.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start = DateTime.UtcNow;
        await _next(context);
        var elapsed = DateTime.UtcNow - start;

        _logger.LogInformation(
            "[{Method}] {Path} -> {StatusCode} ({Elapsed}ms)",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            elapsed.TotalMilliseconds.ToString("F1"));

        // Feed metrics to dashboard
        var metrics = context.RequestServices.GetService<IMetricsTracker>();
        metrics?.TrackRequest(
            context.Request.Path,
            context.Response.StatusCode,
            elapsed.TotalMilliseconds);
    }
}
