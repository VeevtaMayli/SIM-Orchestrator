namespace SIMOrchestrator.Services;

public class RetryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RetryService> _logger;
    private readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);

    public RetryService(IServiceProvider serviceProvider, ILogger<RetryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RetryService started. Interval: {Interval}", _retryInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_retryInterval, stoppingToken);

            try
            {
                await ProcessUnsentMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RetryService");
            }
        }

        _logger.LogInformation("RetryService stopped");
    }

    private async Task ProcessUnsentMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<ISmsStorageService>();
        var telegram = scope.ServiceProvider.GetRequiredService<ITelegramService>();

        var unsentSms = await storage.GetUnsentSmsAsync();

        if (unsentSms.Count == 0)
            return;

        _logger.LogInformation("Retrying {Count} unsent SMS", unsentSms.Count);

        foreach (var sms in unsentSms)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            var success = await telegram.SendSmsToTelegramAsync(sms);
            if (success)
            {
                await storage.MarkAsSentAsync(sms.Id);
            }
        }
    }
}
