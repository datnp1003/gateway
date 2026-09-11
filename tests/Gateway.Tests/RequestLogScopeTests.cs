using Gateway.Domain.Models;
using Gateway.Domain.Interfaces;
using Gateway.Infrastructure.Services;
using Gateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yarp.ReverseProxy.Model;

namespace Gateway.Tests;

public class RequestLogScopeTests
{
    [Theory]
    [InlineData("/api/auth", true)]
    [InlineData("/API/Auth/me", true)]
    [InlineData("/signin-google", true)]
    [InlineData("/api/management/logs/page", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/static/app.js", true)]
    [InlineData("/dashboard/overview", true)]
    [InlineData("/", true)]
    [InlineData("/api/authentication", false)]
    [InlineData("/healthcare", false)]
    [InlineData("/dashboard-api", false)]
    [InlineData("/9r/v1/api/auth", false)]
    [InlineData("/9r/v1/health", false)]
    public void UsesRootSegmentBoundaries(string path, bool excluded) =>
        Assert.Equal(excluded, RequestLogScope.IsInternal(path));

    [Theory]
    [InlineData("/9r/v1", true, 1)]
    [InlineData("/missing", false, 0)]
    [InlineData("/api/auth/me", true, 0)]
    [InlineData("/health", true, 0)]
    public async Task OnlyProxyPipelineResponsesEnterMetricsAndRequestBuffer(string path, bool proxy, int expected)
    {
        var metrics = new MetricsTracker();
        using var services = new ServiceCollection().AddSingleton<IMetricsTracker>(metrics).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        var buffer = new LogBuffer();
        var middleware = new RequestLoggingMiddleware(ctx =>
        {
            if (proxy) ctx.Features.Set<IReverseProxyFeature>(new ReverseProxyFeature());
            return Task.CompletedTask;
        }, NullLogger<RequestLoggingMiddleware>.Instance, buffer);
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, metrics.GetMetrics().TotalRequests);
        Assert.Equal(expected, buffer.GetRecent(50).Count);
    }
}
