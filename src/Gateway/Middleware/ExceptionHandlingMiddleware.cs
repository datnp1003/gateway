using System.Net;
using System.Text.Json;

namespace Gateway.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var error = new
            {
                status = 500,
                title = "Internal Server Error",
                detail = "An unexpected error occurred.",
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(error));
        }
    }
}
