using Microsoft.Extensions.Options;
using SIMOrchestrator.Options;

namespace SIMOrchestrator.Middleware;

public static class ApiKeyServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="ApiKeyOptions"/> from the root configuration
    /// (<c>ApiKey</c> and <c>ApiKeys:N</c>) and validates it when the host starts.
    /// </summary>
    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
    {
        services.AddOptions<ApiKeyOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ApiKey = configuration["ApiKey"];
                options.ApiKeys = configuration.GetSection("ApiKeys").Get<string[]>() ?? [];
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ApiKeyOptions>, ApiKeyOptionsValidator>();
        return services;
    }
}
