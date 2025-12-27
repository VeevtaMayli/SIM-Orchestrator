using System.ComponentModel.DataAnnotations;

namespace SIMOrchestrator.Models;

public class SmsMessage
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Sender { get; set; } = string.Empty;

    [Required]
    public string Text { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Timestamp { get; set; }

    public DateTime ReceivedAt { get; set; }

    public bool SentToTelegram { get; set; }

    public DateTime? SentToTelegramAt { get; set; }
}
