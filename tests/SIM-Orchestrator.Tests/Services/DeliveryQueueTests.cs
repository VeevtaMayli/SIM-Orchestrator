using System.Diagnostics;
using SIMOrchestrator.Services;
using Xunit;

namespace SIMOrchestrator.Tests.Services;

public class DeliveryQueueTests
{
    [Fact]
    public async Task WaitAsync_WithoutSignal_ReturnsAfterTimeout()
    {
        var queue = new DeliveryQueue();
        var watch = Stopwatch.StartNew();

        await queue.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(40), $"returned too early: {watch.Elapsed}");
    }

    [Fact]
    public async Task WaitAsync_AfterSignal_ReturnsImmediately()
    {
        var queue = new DeliveryQueue();
        queue.Signal();

        var wait = queue.WaitAsync(TimeSpan.FromMinutes(5), CancellationToken.None);

        await wait.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WaitAsync_SignalWhileWaiting_WakesTheWaiter()
    {
        var queue = new DeliveryQueue();
        var wait = queue.WaitAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.False(wait.IsCompleted);

        queue.Signal();

        await wait.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Signal_ManyTimes_CollapsesIntoOneWakeUp()
    {
        var queue = new DeliveryQueue();
        for (var i = 0; i < 10; i++)
            queue.Signal();

        await queue.WaitAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
        var watch = Stopwatch.StartNew();
        await queue.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(40), "second wait should have timed out, not been woken");
    }

    [Fact]
    public async Task WaitAsync_Cancelled_Throws()
    {
        var queue = new DeliveryQueue();
        using var cts = new CancellationTokenSource();
        var wait = queue.WaitAsync(TimeSpan.FromMinutes(5), cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
