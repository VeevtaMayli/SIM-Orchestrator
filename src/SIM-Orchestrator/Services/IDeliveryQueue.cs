namespace SIMOrchestrator.Services;

/// <summary>Wakes the delivery dispatcher as soon as new work is stored.</summary>
public interface IDeliveryQueue
{
    void Signal();

    /// <summary>Completes when a signal arrives, when <paramref name="timeout"/> elapses, or throws when cancelled.</summary>
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
