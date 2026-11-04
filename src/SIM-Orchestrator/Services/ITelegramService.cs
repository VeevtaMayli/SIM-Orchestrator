using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

public sealed record TelegramSendResult(bool Success, string? Error = null, TimeSpan? RetryAfter = null)
{
    public static TelegramSendResult Ok() => new(true);

    public static TelegramSendResult Failure(string error, TimeSpan? retryAfter = null) => new(false, error, retryAfter);
}

public interface ITelegramService
{
    Task<TelegramSendResult> SendSmsToTelegramAsync(SmsMessage sms, CancellationToken cancellationToken = default);
}
