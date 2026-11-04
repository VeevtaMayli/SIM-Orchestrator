using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SIMOrchestrator.Data;
using SIMOrchestrator.Models;
using SIMOrchestrator.Options;
using SIMOrchestrator.Services;
using SIMOrchestrator.Tests.TestFixtures;
using Xunit;

namespace SIMOrchestrator.Tests.Services;

public class DeliveryDispatcherTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDb _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly FakeTelegramService _telegram = new();
    private readonly DeliveryOptions _options = new() { DelayBetweenMessages = TimeSpan.Zero, BatchSize = 10, MaxAttempts = 3 };
    private readonly DeliveryQueue _queue = new();

    public void Dispose() => _db.Dispose();

    private DeliveryDispatcher CreateDispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_db.ConnectionString));
        services.AddScoped<ISmsStorageService, SmsStorageService>();
        services.AddSingleton<ITelegramService>(_telegram);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return new DeliveryDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _queue,
            Microsoft.Extensions.Options.Options.Create(_options),
            _time,
            Mock.Of<ILogger<DeliveryDispatcher>>());
    }

    private async Task<SmsMessage> SeedAsync(string text, DeliveryStatus status = DeliveryStatus.Pending, int attempts = 0, DateTime? receivedAt = null)
    {
        using var context = _db.CreateContext();
        var sms = new SmsMessage
        {
            Sender = "+1", Text = text, DedupKey = Guid.NewGuid().ToString(),
            ReceivedAt = receivedAt ?? Now, Status = status, Attempts = attempts
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
    public async Task ProcessPending_Success_MarksSent()
    {
        var sms = await SeedAsync("ok");

        await CreateDispatcher().ProcessPendingAsync(CancellationToken.None);

        var updated = await LoadAsync(sms.Id);
        Assert.Equal(DeliveryStatus.Sent, updated.Status);
        Assert.True(updated.SentToTelegram);
        Assert.Equal(Now, updated.SentToTelegramAt);
        Assert.Single(_telegram.Sent);
    }

    [Fact]
    public async Task ProcessPending_Failure_SchedulesRetryWithBackoff()
    {
        var sms = await SeedAsync("fail");
        _telegram.AlwaysFail("telegram down");

        await CreateDispatcher().ProcessPendingAsync(CancellationToken.None);

        var updated = await LoadAsync(sms.Id);
        Assert.Equal(DeliveryStatus.Pending, updated.Status);
        Assert.Equal(1, updated.Attempts);
        Assert.Equal(Now.AddMinutes(1), updated.NextAttemptAt);
        Assert.Equal("telegram down", updated.LastError);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    public async Task ProcessPending_RepeatedFailures_FollowBackoffSchedule(int previousAttempts, int expectedMinutes)
    {
        _options.MaxAttempts = 10;
        var sms = await SeedAsync("fail", attempts: previousAttempts);
        _telegram.AlwaysFail();

        await CreateDispatcher().ProcessPendingAsync(CancellationToken.None);

        var updated = await LoadAsync(sms.Id);
        Assert.Equal(previousAttempts + 1, updated.Attempts);
        Assert.Equal(Now.AddMinutes(expectedMinutes), updated.NextAttemptAt);
    }

    [Fact]
    public async Task ProcessPending_RetryAfter_OverridesBackoffAndReleasesRestOfBatch()
    {
        var first = await SeedAsync("first", receivedAt: Now.AddMinutes(-2));
        var second = await SeedAsync("second", receivedAt: Now.AddMinutes(-1));
        _telegram.AlwaysFail("429", retryAfter: TimeSpan.FromSeconds(30));

        await CreateDispatcher().ProcessPendingAsync(CancellationToken.None);

        var updatedFirst = await LoadAsync(first.Id);
        Assert.Equal(1, updatedFirst.Attempts);
        Assert.Equal(Now.AddSeconds(30), updatedFirst.NextAttemptAt);

        var updatedSecond = await LoadAsync(second.Id);
        Assert.Equal(DeliveryStatus.Pending, updatedSecond.Status);
        Assert.Equal(0, updatedSecond.Attempts);
        Assert.Equal(Now.AddSeconds(30), updatedSecond.NextAttemptAt);
        Assert.Single(_telegram.Sent);
    }

    [Fact]
    public async Task ProcessPending_MaxAttemptsReached_MarksFailed()
    {
        var sms = await SeedAsync("fail", attempts: _options.MaxAttempts - 1);
        _telegram.AlwaysFail("still down");

        await CreateDispatcher().ProcessPendingAsync(CancellationToken.None);

        var updated = await LoadAsync(sms.Id);
        Assert.Equal(DeliveryStatus.Failed, updated.Status);
        Assert.Equal(_options.MaxAttempts, updated.Attempts);
        Assert.Null(updated.NextAttemptAt);

        // A failed message is never picked up again.
        _telegram.AlwaysSucceed();
        await CreateDispatcher().ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(DeliveryStatus.Failed, (await LoadAsync(sms.Id)).Status);
    }

    [Fact]
    public async Task ProcessPending_NotDueYet_IsSkippedUntilTimeAdvances()
    {
        var sms = await SeedAsync("later");
        _telegram.AlwaysFail();
        var dispatcher = CreateDispatcher();

        await dispatcher.ProcessPendingAsync(CancellationToken.None);
        await dispatcher.ProcessPendingAsync(CancellationToken.None);
        Assert.Single(_telegram.Sent);

        _time.Advance(TimeSpan.FromMinutes(1));
        _telegram.AlwaysSucceed();
        await dispatcher.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(2, _telegram.Sent.Count);
        Assert.Equal(DeliveryStatus.Sent, (await LoadAsync(sms.Id)).Status);
    }

    [Fact]
    public async Task ProcessPending_SendsOldestFirstAndDrainsBeyondBatchSize()
    {
        _options.BatchSize = 2;
        for (var i = 0; i < 5; i++)
            await SeedAsync($"m{i}", receivedAt: Now.AddMinutes(i));

        await CreateDispatcher().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(new[] { "m0", "m1", "m2", "m3", "m4" }, _telegram.Sent.Select(s => s.Text));
    }

    [Fact]
    public async Task ProcessPending_Cancellation_StopsWhileTelegramHangs()
    {
        await SeedAsync("hang");
        using var cts = new CancellationTokenSource();
        _telegram.Handler = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return TelegramSendResult.Ok();
        };
        var dispatcher = CreateDispatcher();

        var run = dispatcher.ProcessPendingAsync(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Signal_WakesTheBackgroundLoopImmediately()
    {
        _options.PollInterval = TimeSpan.FromHours(1);
        var dispatcher = CreateDispatcher();
        await dispatcher.StartAsync(CancellationToken.None);

        try
        {
            await SeedAsync("signalled");
            _queue.Signal();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_telegram.Sent.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Single(_telegram.Sent);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }
}
