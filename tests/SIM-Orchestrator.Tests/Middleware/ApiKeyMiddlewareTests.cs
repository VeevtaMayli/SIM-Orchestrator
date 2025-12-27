using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using SIMOrchestrator.Middleware;
using Xunit;

namespace SIMOrchestrator.Tests.Middleware;

public class ApiKeyMiddlewareTests
{
    private readonly string _validApiKey = "test-api-key-32-characters-long!";

    private ApiKeyMiddleware CreateMiddleware(RequestDelegate next)
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ApiKey"]).Returns(_validApiKey);
        return new ApiKeyMiddleware(next, configMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_MissingApiKey_Returns401()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_InvalidApiKey_Returns403()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = "wrong-key";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ValidApiKey_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-API-Key"] = _validApiKey;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_HealthEndpoint_SkipsAuthentication()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
