using System.Security.Cryptography;
using System.Text;

namespace SIMOrchestrator.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _apiKeyBytes;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var apiKey = configuration["ApiKey"]
            ?? throw new InvalidOperationException("ApiKey not configured");
        _apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-API-Key", out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key missing");
            return;
        }

        // Constant-time comparison to prevent timing attacks
        var extractedBytes = Encoding.UTF8.GetBytes(extractedApiKey.ToString());
        if (!CryptographicOperations.FixedTimeEquals(_apiKeyBytes, extractedBytes))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Invalid API Key");
            return;
        }

        await _next(context);
    }
}
