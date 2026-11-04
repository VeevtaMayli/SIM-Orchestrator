using Microsoft.Extensions.Options;

namespace SIMOrchestrator.Options;

/// <summary>
/// API keys accepted from devices. <see cref="ApiKey"/> is the legacy single key,
/// <see cref="ApiKeys"/> allows several active keys so that a key can be rotated
/// without downtime (add the new key, update devices, remove the old key).
/// </summary>
public sealed class ApiKeyOptions
{
    public const int MinimumKeyLength = 32;

    public string? ApiKey { get; set; }

    public string[] ApiKeys { get; set; } = [];

    /// <summary>Path prefixes that do not require an API key (health checks, dev tooling).</summary>
    public string[] AnonymousPathPrefixes { get; set; } = ["/health"];

    public IReadOnlyList<string> GetAllKeys()
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApiKey))
            keys.Add(ApiKey);
        keys.AddRange(ApiKeys.Where(k => !string.IsNullOrWhiteSpace(k)));
        return keys.Distinct(StringComparer.Ordinal).ToList();
    }
}

public sealed class ApiKeyOptionsValidator : IValidateOptions<ApiKeyOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiKeyOptions options)
    {
        var keys = options.GetAllKeys();
        if (keys.Count == 0)
            return ValidateOptionsResult.Fail("No API key configured. Set 'ApiKey' or 'ApiKeys:0'.");

        if (keys.Any(k => k.Length < ApiKeyOptions.MinimumKeyLength))
            return ValidateOptionsResult.Fail(
                $"Every API key must be at least {ApiKeyOptions.MinimumKeyLength} characters long.");

        return ValidateOptionsResult.Success;
    }
}
