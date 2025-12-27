using System.ComponentModel.DataAnnotations;

namespace SIMOrchestrator.Models;

public class SmsRequest
{
    [Required]
    [MaxLength(50)]
    public string Sender { get; set; } = string.Empty;

    [Required]
    public string Text { get; set; } = string.Empty;

    public string? Timestamp { get; set; }
}
