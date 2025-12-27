using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

public interface ISmsStorageService
{
    Task<SmsMessage> SaveSmsAsync(SmsRequest request);
    Task<List<SmsMessage>> GetUnsentSmsAsync();
    Task MarkAsSentAsync(int smsId);
}
