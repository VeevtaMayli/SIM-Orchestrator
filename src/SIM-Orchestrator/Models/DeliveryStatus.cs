namespace SIMOrchestrator.Models;

public enum DeliveryStatus
{
    /// <summary>Waiting to be delivered (possibly not before <see cref="SmsMessage.NextAttemptAt"/>).</summary>
    Pending = 0,

    /// <summary>Claimed by the dispatcher; the lease expires at <see cref="SmsMessage.LeaseExpiresAt"/>.</summary>
    InProgress = 1,

    /// <summary>Delivered to Telegram.</summary>
    Sent = 2,

    /// <summary>Gave up after the configured number of attempts; needs human attention.</summary>
    Failed = 3
}
