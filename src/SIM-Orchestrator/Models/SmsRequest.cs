using System.ComponentModel.DataAnnotations;

namespace SIMOrchestrator.Models;

public class SmsRequest
{
    /// <summary>
    /// Upper bound for a (concatenated) SMS. Telegram messages are limited to
    /// 4096 characters, so anything above this could never be delivered.
    /// </summary>
    public const int MaxTextLength = 4000;

    public const int MaxMessageIdLength = 64;

    [Required]
    [MaxLength(50)]
    public string Sender { get; set; } = string.Empty;

    [Required]
    [MaxLength(MaxTextLength)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Optional identifier assigned by the device. When present it makes retries
    /// from the device idempotent even if the text or timestamp were re-encoded.
    /// </summary>
    [MaxLength(MaxMessageIdLength)]
    public string? MessageId { get; set; }
}
