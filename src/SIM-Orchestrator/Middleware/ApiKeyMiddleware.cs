using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SIMOrchestrator.Options;

namespace SIMOrchestrator.Middleware;

public class ApiKeyMiddleware
{
    public const string HeaderName = "X-API-Key";
    private const string Challenge = "ApiKey realm=\"SIM-Orchestrator\"";

    private readonly RequestDelegate _next;
    private readonly byte[][] _keyHashes;
    private readonly PathString[] _anonymousPrefixes;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options)
    {
        _next = next;

        var value = options.Value;
        _keyHashes = value.GetAllKeys().Select(Hash).ToArray();
        if (_keyHashes.Length == 0)
            throw new InvalidOperationException("No API key configured");

        _anonymousPrefixes = value.AnonymousPathPrefixes
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new PathString(p))
            .ToArray();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAnonymous(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var values)
            || values.Count == 0
            || string.IsNullOrEmpty(values[0]))
        {
            await RejectAsync(context, "API Key missing");
            return;
        }

        // A request that sends the header several times is never valid.
        if (values.Count != 1 || !IsValidKey(values[0]!))
        {
            await RejectAsync(context, "Invalid API Key");
            return;
        }

        await _next(context);
    }

    private bool IsAnonymous(PathString path)
    {
        foreach (var prefix in _anonymousPrefixes)
        {
            if (path.StartsWithSegments(prefix))
                return true;
        }
        return false;
    }

    private bool IsValidKey(string candidate)
    {
        // Hashing both sides first means the comparison never depends on the
        // length of the configured key, and every configured key is checked so
        // the timing does not reveal which one matched.
        var candidateHash = Hash(candidate);
        var matched = false;
        foreach (var hash in _keyHashes)
        {
            matched |= CryptographicOperations.FixedTimeEquals(hash, candidateHash);
        }
        return matched;
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static Task RejectAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = Challenge;
        return context.Response.WriteAsync(message);
    }
}
