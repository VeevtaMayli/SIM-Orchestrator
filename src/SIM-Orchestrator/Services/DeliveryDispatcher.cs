using Microsoft.Extensions.Options;
using SIMOrchestrator.Options;

namespace SIMOrchestrator.Services;

/// <summary>
/// Outbox worker: delivers stored SMS to Telegram with retries, backoff and a
/// bounded number of attempts. The HTTP request path only stores and signals.
/// </summary>
public sealed class DeliveryDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeliveryQueue _queue;
    private readonly DeliveryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeliveryDispatcher> _logger;

    public DeliveryDispatcher(
        IServiceScopeFactory scopeFactory,
        IDeliveryQueue queue,
        IOptions<DeliveryOptions> options,
        TimeProvider timeProvider,
        ILogger<DeliveryDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Delivery dispatcher started. Poll interval: {PollInterval}, max attempts: {MaxAttempts}",
            _options.PollInterval, _options.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delivery pass failed");
            }

            try
            {
                await _queue.WaitAsync(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Delivery dispatcher stopped");
    }

    /// <summary>Delivers everything that is due right now. Returns when no due message is left or on cancellation.</summary>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<ISmsStorageService>();
            var telegram = scope.ServiceProvider.GetRequiredService<ITelegramService>();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var batch = await storage.ClaimDueAsync(_options.BatchSize, now, _options.LeaseDuration, cancellationToken);
            if (batch.Count == 0)
                return;

            _logger.LogInformation("Delivering {Count} SMS", batch.Count);

            for (var i = 0; i < batch.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sms = batch[i];

                var result = await telegram.SendSmsToTelegramAsync(sms, cancellationToken);
                var finishedAt = _timeProvider.GetUtcNow().UtcDateTime;

                if (result.Success)
                {
                    await storage.MarkSentAsync(sms.Id, finishedAt, cancellationToken);
                }
                else
                {
                    await RecordFailureAsync(storage, sms.Id, sms.Attempts + 1, result, finishedAt, cancellationToken);

                    if (result.RetryAfter is { } retryAfter)
                    {
                        // Telegram asked us to back off: do not hammer it with the rest of the batch.
                        var remaining = batch.Skip(i + 1).Select(s => s.Id);
                        await storage.ReleaseAsync(remaining, finishedAt + retryAfter, cancellationToken);
                        return;
                    }
                }

                if (_options.DelayBetweenMessages > TimeSpan.Zero && i < batch.Count - 1)
                    await Task.Delay(_options.DelayBetweenMessages, _timeProvider, cancellationToken);
            }

            if (batch.Count < _options.BatchSize)
                return;
        }
    }

    private async Task RecordFailureAsync(
        ISmsStorageService storage,
        int smsId,
        int attempts,
        TelegramSendResult result,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var error = result.Error ?? "unknown error";

        if (attempts >= _options.MaxAttempts)
        {
            await storage.MarkAttemptFailedAsync(smsId, error, nextAttemptAt: null, cancellationToken);
            _logger.LogError(
                "SMS {Id} failed permanently after {Attempts} attempts: {Error}", smsId, attempts, error);
            return;
        }

        var delay = result.RetryAfter ?? _options.GetBackoff(attempts);
        var nextAttemptAt = now + delay;
        await storage.MarkAttemptFailedAsync(smsId, error, nextAttemptAt, cancellationToken);
        _logger.LogWarning(
            "SMS {Id} delivery attempt {Attempts} failed, next attempt at {NextAttemptAt:O}: {Error}",
            smsId, attempts, nextAttemptAt, error);
    }
}
