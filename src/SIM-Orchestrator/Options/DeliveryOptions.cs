using System.ComponentModel.DataAnnotations;

namespace SIMOrchestrator.Options;

/// <summary>Tuning for the outbox dispatcher that delivers stored SMS to Telegram.</summary>
public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    /// <summary>How often the dispatcher looks for due messages when nothing wakes it up earlier.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>After this many failed attempts a message is marked <c>Failed</c> and no longer retried.</summary>
    [Range(1, 1000)]
    public int MaxAttempts { get; set; } = 20;

    /// <summary>How long a claimed message stays <c>InProgress</c> before another pass may pick it up again.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Pause between consecutive Telegram sends (Telegram recommends about one message per second per chat).</summary>
    public TimeSpan DelayBetweenMessages { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Delay before attempt N+1 after N failures; the last value repeats.</summary>
    public int[] BackoffMinutes { get; set; } = [1, 5, 15, 60];

    public TimeSpan GetBackoff(int failedAttempts)
    {
        if (BackoffMinutes.Length == 0)
            return PollInterval;

        var index = Math.Clamp(failedAttempts - 1, 0, BackoffMinutes.Length - 1);
        return TimeSpan.FromMinutes(BackoffMinutes[index]);
    }
}
