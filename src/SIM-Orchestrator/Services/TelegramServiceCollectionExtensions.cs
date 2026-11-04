using Microsoft.Extensions.Options;
using SIMOrchestrator.Options;

namespace SIMOrchestrator.Services;

public static class TelegramServiceCollectionExtensions
{
    public const string TelegramApiBaseUrl = "https://api.telegram.org/";

    /// <summary>
    /// Registers the Telegram client. The bot token lives only in the client's
    /// BaseAddress, and the HttpClientFactory request loggers are removed so the
    /// token is never written to logs as part of a request URI.
    /// </summary>
    public static IHttpClientBuilder AddTelegramClient(this IServiceCollection services)
    {
        services.AddOptions<TelegramOptions>()
            .BindConfiguration(TelegramOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services
            .AddHttpClient<ITelegramService, TelegramService>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<TelegramOptions>>().Value;
                client.BaseAddress = new Uri($"{TelegramApiBaseUrl}bot{options.BotToken}/");
            })
            .RemoveAllLoggers();
    }
}
