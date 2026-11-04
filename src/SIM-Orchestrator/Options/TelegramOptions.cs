using System.ComponentModel.DataAnnotations;

namespace SIMOrchestrator.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    [Required(AllowEmptyStrings = false)]
    public string BotToken { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ChatId { get; set; } = string.Empty;
}
