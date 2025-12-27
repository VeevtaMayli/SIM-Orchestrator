using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

public interface ITelegramService
{
    Task<bool> SendSmsToTelegramAsync(SmsMessage sms);
}
