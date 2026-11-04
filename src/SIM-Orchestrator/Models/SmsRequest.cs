using System.ComponentModel.DataAnnotations;

namespace SIMOrchestrator.Models;

public class SmsRequest
{
    [Required]
    [MaxLength(50)]
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// Upper bound for a (concatenated) SMS. Telegram messages are limited to
    /// 4096 characters, so anything above this could never be delivered.
    /// </summary>
    public const int MaxTextLength = 4000;

    [Required]
    [MaxLength(MaxTextLength)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Timestamp { get; set; }
}
