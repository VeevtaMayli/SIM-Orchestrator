using SIMOrchestrator.Models;
using SIMOrchestrator.Services;

namespace SIMOrchestrator.Tests.TestFixtures;

/// <summary>In-memory stand-in for Telegram: records what was sent and answers with a programmable result.</summary>
public sealed class FakeTelegramService : ITelegramService
{
    private readonly List<SmsMessage> _sent = new();

    /// <summary>Decides the outcome of each send; defaults to success.</summary>
    public Func<SmsMessage, CancellationToken, Task<TelegramSendResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(TelegramSendResult.Ok());

    /// <summary>Every message handed to the service, in call order (including failed ones).</summary>
    public IReadOnlyList<SmsMessage> Sent
    {
        get
        {
            lock (_sent)
                return _sent.ToList();
        }
    }

    public void AlwaysFail(string error = "boom", TimeSpan? retryAfter = null) =>
        Handler = (_, _) => Task.FromResult(TelegramSendResult.Failure(error, retryAfter));

    public void AlwaysSucceed() => Handler = (_, _) => Task.FromResult(TelegramSendResult.Ok());

    public async Task<TelegramSendResult> SendSmsToTelegramAsync(SmsMessage sms, CancellationToken cancellationToken = default)
    {
        lock (_sent)
            _sent.Add(sms);
        return await Handler(sms, cancellationToken);
    }
}
