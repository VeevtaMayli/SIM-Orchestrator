using Microsoft.EntityFrameworkCore;
using SIMOrchestrator.Data;
using SIMOrchestrator.Logging;
using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

public class SmsStorageService : ISmsStorageService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SmsStorageService> _logger;

    public SmsStorageService(AppDbContext context, TimeProvider timeProvider, ILogger<SmsStorageService> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SaveSmsResult> SaveSmsAsync(SmsRequest request, CancellationToken cancellationToken = default)
    {
        var key = DedupKey.Compute(request);

        var existing = await FindByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Duplicate SMS ignored. ID: {Id}, From: {Sender}", existing.Id, SenderMasker.Mask(existing.Sender));
            return new SaveSmsResult(existing, IsDuplicate: true);
        }

        var sms = new SmsMessage
        {
            Sender = request.Sender,
            Text = request.Text,
            Timestamp = request.Timestamp,
            DedupKey = key,
            ReceivedAt = _timeProvider.GetUtcNow().UtcDateTime,
            Status = DeliveryStatus.Pending,
            SentToTelegram = false
        };

        _context.SmsMessages.Add(sms);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two identical requests raced past the lookup above; the unique index
            // on DedupKey let only one of them in. Return the winner.
            _context.Entry(sms).State = EntityState.Detached;
            var winner = await FindByKeyAsync(key, cancellationToken);
            if (winner is null)
                throw;

            _logger.LogInformation("Duplicate SMS ignored after insert race. ID: {Id}", winner.Id);
            return new SaveSmsResult(winner, IsDuplicate: true);
        }

        _logger.LogInformation("SMS saved. ID: {Id}, From: {Sender}", sms.Id, SenderMasker.Mask(sms.Sender));
        return new SaveSmsResult(sms, IsDuplicate: false);
    }

    public async Task<List<SmsMessage>> ClaimDueAsync(int batchSize, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var due = await _context.SmsMessages
            .Where(s =>
                (s.Status == DeliveryStatus.Pending && (s.NextAttemptAt == null || s.NextAttemptAt <= now))
                || (s.Status == DeliveryStatus.InProgress && s.LeaseExpiresAt != null && s.LeaseExpiresAt < now))
            .OrderBy(s => s.ReceivedAt)
            .ThenBy(s => s.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
            return due;

        var leaseExpiresAt = now + lease;
        foreach (var sms in due)
        {
            sms.Status = DeliveryStatus.InProgress;
            sms.LeaseExpiresAt = leaseExpiresAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return due;
    }

    public async Task MarkSentAsync(int smsId, DateTime sentAt, CancellationToken cancellationToken = default)
    {
        var sms = await _context.SmsMessages.FindAsync([smsId], cancellationToken);
        if (sms is null)
            return;

        sms.Status = DeliveryStatus.Sent;
        sms.SentToTelegram = true;
        sms.SentToTelegramAt = sentAt;
        sms.Attempts += 1;
        sms.NextAttemptAt = null;
        sms.LeaseExpiresAt = null;
        sms.LastError = null;

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("SMS marked as sent. ID: {Id}, Attempts: {Attempts}", smsId, sms.Attempts);
    }

    public async Task MarkAttemptFailedAsync(int smsId, string error, DateTime? nextAttemptAt, CancellationToken cancellationToken = default)
    {
        var sms = await _context.SmsMessages.FindAsync([smsId], cancellationToken);
        if (sms is null)
            return;

        sms.Attempts += 1;
        sms.LastError = Truncate(error, SmsMessage.MaxLastErrorLength);
        sms.LeaseExpiresAt = null;
        sms.SentToTelegram = false;

        if (nextAttemptAt is null)
        {
            sms.Status = DeliveryStatus.Failed;
            sms.NextAttemptAt = null;
        }
        else
        {
            sms.Status = DeliveryStatus.Pending;
            sms.NextAttemptAt = nextAttemptAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(IEnumerable<int> smsIds, DateTime nextAttemptAt, CancellationToken cancellationToken = default)
    {
        var ids = smsIds.ToList();
        if (ids.Count == 0)
            return;

        var claimed = await _context.SmsMessages
            .Where(s => ids.Contains(s.Id) && s.Status == DeliveryStatus.InProgress)
            .ToListAsync(cancellationToken);

        foreach (var sms in claimed)
        {
            sms.Status = DeliveryStatus.Pending;
            sms.LeaseExpiresAt = null;
            sms.NextAttemptAt = nextAttemptAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountUnsentAsync(CancellationToken cancellationToken = default)
    {
        return _context.SmsMessages.CountAsync(
            s => s.Status == DeliveryStatus.Pending || s.Status == DeliveryStatus.InProgress,
            cancellationToken);
    }

    public Task<int> CountFailedAsync(CancellationToken cancellationToken = default)
    {
        return _context.SmsMessages.CountAsync(s => s.Status == DeliveryStatus.Failed, cancellationToken);
    }

    private Task<SmsMessage?> FindByKeyAsync(string key, CancellationToken cancellationToken)
        => _context.SmsMessages.FirstOrDefaultAsync(s => s.DedupKey == key, cancellationToken);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
