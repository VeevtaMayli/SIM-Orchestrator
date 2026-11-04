using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

public sealed record SaveSmsResult(SmsMessage Sms, bool IsDuplicate);

public interface ISmsStorageService
{
    /// <summary>Stores the SMS, or returns the already stored one when the request is a retry.</summary>
    Task<SaveSmsResult> SaveSmsAsync(SmsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks up to <paramref name="batchSize"/> messages that are due (pending and
    /// <see cref="SmsMessage.NextAttemptAt"/> reached, or in progress with an expired
    /// lease), marks them in progress until <paramref name="now"/> + <paramref name="lease"/>
    /// and returns them oldest first.
    /// </summary>
    Task<List<SmsMessage>> ClaimDueAsync(int batchSize, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default);

    Task MarkSentAsync(int smsId, DateTime sentAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed attempt. With <paramref name="nextAttemptAt"/> the message goes
    /// back to pending; with null it is marked failed and never retried automatically.
    /// </summary>
    Task MarkAttemptFailedAsync(int smsId, string error, DateTime? nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>Returns claimed messages to pending without counting an attempt (for example after a 429).</summary>
    Task ReleaseAsync(IEnumerable<int> smsIds, DateTime nextAttemptAt, CancellationToken cancellationToken = default);

    Task<int> CountUnsentAsync(CancellationToken cancellationToken = default);

    Task<int> CountFailedAsync(CancellationToken cancellationToken = default);
}
