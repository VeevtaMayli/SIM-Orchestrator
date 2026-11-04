using Microsoft.AspNetCore.Http;
using SIMOrchestrator.Middleware;
using SIMOrchestrator.Options;
using Xunit;

namespace SIMOrchestrator.Tests.Middleware;

public class ApiKeyMiddlewareTests
{
    private const string PrimaryKey = "test-api-key-32-characters-long!";
    private const string RotatedKey = "second-api-key-for-rotation-abcdef";

    private static ApiKeyMiddleware CreateMiddleware(RequestDelegate next, ApiKeyOptions? options = null)
    {
        options ??= new ApiKeyOptions { ApiKey = PrimaryKey };
        return new ApiKeyMiddleware(next, Microsoft.Extensions.Options.Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string path = "/api/sms")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_MissingApiKey_Returns401WithChallenge()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Contains("ApiKey", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task InvokeAsync_InvalidApiKey_Returns401()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext();
        context.Request.Headers["X-API-Key"] = "wrong-key";

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_KeyWithSameLengthButDifferentContent_Returns401()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateContext();
        context.Request.Headers["X-API-Key"] = new string('x', PrimaryKey.Length);

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ValidApiKey_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext();
        context.Request.Headers["X-API-Key"] = PrimaryKey;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_HeaderSentTwice_Returns401()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext();
        context.Request.Headers["X-API-Key"] = new[] { PrimaryKey, PrimaryKey };

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_AnyKeyFromRotationList_IsAccepted()
    {
        var options = new ApiKeyOptions { ApiKey = PrimaryKey, ApiKeys = [RotatedKey] };
        var calls = 0;
        var middleware = CreateMiddleware(_ => { calls++; return Task.CompletedTask; }, options);

        foreach (var key in new[] { PrimaryKey, RotatedKey })
        {
            var context = CreateContext();
            context.Request.Headers["X-API-Key"] = key;
            await middleware.InvokeAsync(context);
            Assert.Equal(200, context.Response.StatusCode);
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task InvokeAsync_LegacyApiKeyWithoutList_StillWorks()
    {
        var options = new ApiKeyOptions { ApiKey = PrimaryKey, ApiKeys = [] };
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, options);
        var context = CreateContext();
        context.Request.Headers["X-API-Key"] = PrimaryKey;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/swagger/index.html")]
    public async Task InvokeAsync_AnonymousPrefix_SkipsAuthentication(string path)
    {
        var options = new ApiKeyOptions { ApiKey = PrimaryKey, AnonymousPathPrefixes = ["/health", "/swagger"] };
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, options);
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_PathThatMerelyStartsWithHealth_StillRequiresKey()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/healthz-admin");

        await middleware.InvokeAsync(context);

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public void Constructor_WithoutKeys_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateMiddleware(_ => Task.CompletedTask, new ApiKeyOptions()));
    }
}
