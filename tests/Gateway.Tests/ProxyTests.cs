using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.Tests;

public class ProxyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProxyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthRoute_ForwardsWithoutAuth()
    {
        // /api/auth/* is public, YARP should try to proxy (502 = no backend)
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/register");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task LearningRoute_WithoutToken_Returns401()
    {
        // /api/learning/* requires Authenticated policy
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/learning/courses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/nonexistent/thing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AllProtectedRoutes_RejectUnauthenticated()
    {
        // All service routes except auth should require authentication
        var protectedPaths = new[]
        {
            "/api/learning/courses",
            "/api/ai/generate",
            "/api/writing/submit",
            "/api/speaking/practice",
            "/api/gamification/points"
        };

        var client = _factory.CreateClient();

        foreach (var path in protectedPaths)
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
