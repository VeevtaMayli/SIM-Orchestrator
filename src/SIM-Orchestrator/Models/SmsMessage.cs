using System.ComponentModel.DataAnnotations;

namespace SIMOrchestrator.Models;

public class SmsMessage
{
    public const int MaxDedupKeyLength = 80;
    public const int MaxLastErrorLength = 500;

    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Sender { get; set; } = string.Empty;

    [Required]
    public string Text { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Timestamp { get; set; }

    /// <summary>UTC time the server accepted the message.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>Kept in sync with <see cref="Status"/> == <see cref="DeliveryStatus.Sent"/>.</summary>
    public bool SentToTelegram { get; set; }

    public DateTime? SentToTelegramAt { get; set; }

    /// <summary>Identifies the SMS independent of retries from the device; unique.</summary>
    [Required]
    [MaxLength(MaxDedupKeyLength)]
    public string DedupKey { get; set; } = string.Empty;

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    /// <summary>Number of delivery attempts made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>UTC time before which the dispatcher must not retry; null means "as soon as possible".</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>UTC time the current <see cref="DeliveryStatus.InProgress"/> claim expires.</summary>
    public DateTime? LeaseExpiresAt { get; set; }

    [MaxLength(MaxLastErrorLength)]
    public string? LastError { get; set; }
}
