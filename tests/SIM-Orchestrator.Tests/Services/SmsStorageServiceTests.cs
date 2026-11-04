using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SIMOrchestrator.Data;
using SIMOrchestrator.Models;
using SIMOrchestrator.Services;
using SIMOrchestrator.Tests.TestFixtures;
using Xunit;

namespace SIMOrchestrator.Tests.Services;

public class SmsStorageServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDb _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    public void Dispose() => _db.Dispose();

    private SmsStorageService CreateService(AppDbContext context) =>
        new(context, _time, Mock.Of<ILogger<SmsStorageService>>());

    private async Task<SmsMessage> SeedAsync(
        DeliveryStatus status,
        DateTime? nextAttemptAt = null,
        DateTime? leaseExpiresAt = null,
        DateTime? receivedAt = null,
        string? key = null)
    {
        using var context = _db.CreateContext();
        var sms = new SmsMessage
        {
            Sender = "+1",
            Text = "t",
            DedupKey = key ?? Guid.NewGuid().ToString(),
            ReceivedAt = receivedAt ?? Now,
            Status = status,
            SentToTelegram = status == DeliveryStatus.Sent,
            NextAttemptAt = nextAttemptAt,
            LeaseExpiresAt = leaseExpiresAt
        };
        context.SmsMessages.Add(sms);
        await context.SaveChangesAsync();
        return sms;
    }

    private async Task<SmsMessage> LoadAsync(int id)
    {
        using var context = _db.CreateContext();
        return (await context.SmsMessages.FindAsync(id))!;
    }

    [Fact]
    public async Task SaveSmsAsync_NewRequest_SavesAsPendingWithUtcReceivedAt()
    {
        using var context = _db.CreateContext();
        var service = CreateService(context);
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test", Timestamp = "2026-01-15 11:59:00" };

        var result = await service.SaveSmsAsync(request);

        Assert.False(result.IsDuplicate);
        Assert.NotEqual(0, result.Sms.Id);
        Assert.Equal(DeliveryStatus.Pending, result.Sms.Status);
        Assert.Equal(Now, result.Sms.ReceivedAt);
        Assert.Equal(DedupKey.Compute(request), result.Sms.DedupKey);

        var stored = await LoadAsync(result.Sms.Id);
        Assert.Equal(DateTimeKind.Utc, stored.ReceivedAt.Kind);
    }

    [Fact]
    public async Task SaveSmsAsync_SameContentTwice_ReturnsDuplicateWithOriginalId()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Same", Timestamp = "ts" };

        int firstId;
        using (var context = _db.CreateContext())
            firstId = (await CreateService(context).SaveSmsAsync(request)).Sms.Id;

        using var second = _db.CreateContext();
        var result = await CreateService(second).SaveSmsAsync(new SmsRequest { Sender = "+1234567890", Text = "Same", Timestamp = "ts" });

        Assert.True(result.IsDuplicate);
        Assert.Equal(firstId, result.Sms.Id);
        Assert.Equal(1, second.SmsMessages.Count());
    }

    [Fact]
    public async Task SaveSmsAsync_InsertRace_ReturnsWinnerAsDuplicate()
    {
        var request = new SmsRequest { Sender = "+1", Text = "race" };
        var key = DedupKey.Compute(request);

        // The interceptor inserts a row with the same key right after this
        // context's lookup query ran, so the lookup misses but the insert collides.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_db.ConnectionString)
            .AddInterceptors(new InsertAfterLookupInterceptor(_db, key))
            .Options;
        using var context = new AppDbContext(options);

        var result = await CreateService(context).SaveSmsAsync(request);

        Assert.True(result.IsDuplicate);
        Assert.Equal("inserted-by-race", result.Sms.Text);
        using var verify = _db.CreateContext();
        Assert.Equal(1, verify.SmsMessages.Count(s => s.DedupKey == key));
    }

    private sealed class InsertAfterLookupInterceptor(SqliteTestDb db, string key) : DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!_done && command.CommandText.Contains("DedupKey") && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                _done = true;
                await using var other = db.CreateContext();
                other.SmsMessages.Add(new SmsMessage { Sender = "+1", Text = "inserted-by-race", DedupKey = key, ReceivedAt = Now });
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task ClaimDueAsync_PicksDuePendingAndExpiredLeasesOldestFirst()
    {
        var oldest = await SeedAsync(DeliveryStatus.Pending, receivedAt: Now.AddMinutes(-10));
        var due = await SeedAsync(DeliveryStatus.Pending, nextAttemptAt: Now.AddSeconds(-1), receivedAt: Now.AddMinutes(-5));
        var expiredLease = await SeedAsync(DeliveryStatus.InProgress, leaseExpiresAt: Now.AddSeconds(-1), receivedAt: Now.AddMinutes(-1));
        var notYetDue = await SeedAsync(DeliveryStatus.Pending, nextAttemptAt: Now.AddMinutes(5));
        var liveLease = await SeedAsync(DeliveryStatus.InProgress, leaseExpiresAt: Now.AddMinutes(1));
        var sent = await SeedAsync(DeliveryStatus.Sent);
        var failed = await SeedAsync(DeliveryStatus.Failed);

        using var context = _db.CreateContext();
        var claimed = await CreateService(context).ClaimDueAsync(10, Now, TimeSpan.FromMinutes(2));

        Assert.Equal(new[] { oldest.Id, due.Id, expiredLease.Id }, claimed.Select(s => s.Id));
        Assert.All(claimed, s =>
        {
            Assert.Equal(DeliveryStatus.InProgress, s.Status);
            Assert.Equal(Now.AddMinutes(2), s.LeaseExpiresAt);
        });
        foreach (var untouched in new[] { notYetDue, liveLease, sent, failed })
            Assert.Equal(untouched.Status, (await LoadAsync(untouched.Id)).Status);
    }

    [Fact]
    public async Task ClaimDueAsync_RespectsBatchSize()
    {
        for (var i = 0; i < 5; i++)
            await SeedAsync(DeliveryStatus.Pending, receivedAt: Now.AddMinutes(i));

        using var context = _db.CreateContext();
        var claimed = await CreateService(context).ClaimDueAsync(2, Now.AddHours(1), TimeSpan.FromMinutes(2));

        Assert.Equal(2, claimed.Count);
    }

    [Fact]
    public async Task MarkSentAsync_UpdatesStatusAndLegacyFlag()
    {
        var sms = await SeedAsync(DeliveryStatus.InProgress, leaseExpiresAt: Now.AddMinutes(2));

        using var context = _db.CreateContext();
        await CreateService(context).MarkSentAsync(sms.Id, Now);

        var updated = await LoadAsync(sms.Id);
        Assert.Equal(DeliveryStatus.Sent, updated.Status);
        Assert.True(updated.SentToTelegram);
        Assert.Equal(Now, updated.SentToTelegramAt);
        Assert.Equal(1, updated.Attempts);
        Assert.Null(updated.LeaseExpiresAt);
        Assert.Null(updated.NextAttemptAt);
    }

    [Fact]
    public async Task MarkAttemptFailedAsync_WithNextAttempt_ReturnsToPending()
    {
        var sms = await SeedAsync(DeliveryStatus.InProgress, leaseExpiresAt: Now.AddMinutes(2));
        var next = Now.AddMinutes(5);

        using var context = _db.CreateContext();
        await CreateService(context).MarkAttemptFailedAsync(sms.Id, "telegram down", next);

        var updated = await LoadAsync(sms.Id);
        Assert.Equal(DeliveryStatus.Pending, updated.Status);
        Assert.Equal(1, updated.Attempts);
        Assert.Equal(next, updated.NextAttemptAt);
        Assert.Equal("telegram down", updated.LastError);
        Assert.Null(updated.LeaseExpiresAt);
        Assert.False(updated.SentToTelegram);
    }

    [Fact]
    public async Task MarkAttemptFailedAsync_WithoutNextAttempt_MarksFailedAndTruncatesError()
    {
        var sms = await SeedAsync(DeliveryStatus.InProgress);
        var longError = new string('e', 2000);

        using var context = _db.CreateContext();
        await CreateService(context).MarkAttemptFailedAsync(sms.Id, longError, null);

        var updated = await LoadAsync(sms.Id);
        Assert.Equal(DeliveryStatus.Failed, updated.Status);
        Assert.Equal(SmsMessage.MaxLastErrorLength, updated.LastError!.Length);
        Assert.Null(updated.NextAttemptAt);
    }

    [Fact]
    public async Task ReleaseAsync_ReturnsOnlyInProgressMessagesToPending()
    {
        var claimed = await SeedAsync(DeliveryStatus.InProgress, leaseExpiresAt: Now.AddMinutes(2));
        var sent = await SeedAsync(DeliveryStatus.Sent);
        var next = Now.AddSeconds(30);

        using var context = _db.CreateContext();
        await CreateService(context).ReleaseAsync([claimed.Id, sent.Id], next);

        var released = await LoadAsync(claimed.Id);
        Assert.Equal(DeliveryStatus.Pending, released.Status);
        Assert.Equal(next, released.NextAttemptAt);
        Assert.Equal(0, released.Attempts);
        Assert.Equal(DeliveryStatus.Sent, (await LoadAsync(sent.Id)).Status);
    }

    [Fact]
    public async Task Counts_ReflectStatuses()
    {
        await SeedAsync(DeliveryStatus.Pending);
        await SeedAsync(DeliveryStatus.InProgress);
        await SeedAsync(DeliveryStatus.Sent);
        await SeedAsync(DeliveryStatus.Failed);
        await SeedAsync(DeliveryStatus.Failed);

        using var context = _db.CreateContext();
        var service = CreateService(context);

        Assert.Equal(2, await service.CountUnsentAsync());
        Assert.Equal(2, await service.CountFailedAsync());
    }
}
