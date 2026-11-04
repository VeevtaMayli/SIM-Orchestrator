using SIMOrchestrator.Models;
using SIMOrchestrator.Services;

namespace SIMOrchestrator.Tests.TestFixtures;

/// <summary>In-memory stand-in for Telegram: records what was sent and returns a configurable result.</summary>
public sealed class FakeTelegramService : ITelegramService
{
    private readonly List<SmsMessage> _sent = new();

    public bool Result { get; set; } = true;

    public IReadOnlyList<SmsMessage> Sent
    {
        get
        {
            lock (_sent)
                return _sent.ToList();
        }
    }

    public Task<bool> SendSmsToTelegramAsync(SmsMessage sms)
    {
        lock (_sent)
            _sent.Add(sms);
        return Task.FromResult(Result);
    }
}
