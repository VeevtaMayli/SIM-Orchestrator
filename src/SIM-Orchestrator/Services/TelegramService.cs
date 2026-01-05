using System.Text;
using System.Text.Json;
using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

public class TelegramService : ITelegramService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramService> _logger;
    private readonly string _botToken;
    private readonly string _chatId;

    public TelegramService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TelegramService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _botToken = configuration["Telegram:BotToken"]
            ?? throw new InvalidOperationException("Telegram:BotToken not configured");
        _chatId = configuration["Telegram:ChatId"]
            ?? throw new InvalidOperationException("Telegram:ChatId not configured");
    }

    public async Task<bool> SendSmsToTelegramAsync(SmsMessage sms)
    {
        try
        {
            var message = FormatMessage(sms);
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";

            var payload = new
            {
                chat_id = _chatId,
                text = message,
                parse_mode = "HTML"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending SMS to Telegram. ID: {Id}", sms.Id);

            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("SMS sent to Telegram. ID: {Id}", sms.Id);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Telegram API error. Status: {Status}, Response: {Response}",
                response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to Telegram. ID: {Id}", sms.Id);
            return false;
        }
    }

    private static string FormatMessage(SmsMessage sms)
    {
        string sentTimeFormatted;
        if (!string.IsNullOrEmpty(sms.Timestamp) &&
            DateTimeOffset.TryParse(sms.Timestamp, out var sentTime))
        {
            sentTimeFormatted = sentTime.ToString("yyyy-MM-dd HH:mm:ss zzz");
        }
        else
        {
            // Fallback: show raw timestamp or ReceivedAt
            sentTimeFormatted = string.IsNullOrEmpty(sms.Timestamp)
                ? new DateTimeOffset(sms.ReceivedAt).ToString("yyyy-MM-dd HH:mm:ss zzz")
                : sms.Timestamp;
        }

        // Show ReceivedAt with server timezone (assume UTC if Kind is Unspecified)
        var receivedTime = sms.ReceivedAt.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(sms.ReceivedAt, TimeSpan.Zero)
            : new DateTimeOffset(sms.ReceivedAt);
        var receivedTimeFormatted = receivedTime.ToString("yyyy-MM-dd HH:mm:ss zzz");

        // Format text: highlight numbers with monospace for easy copying (OTP codes, etc.)
        var formattedText = HighlightNumbers(EscapeHtml(sms.Text));

        return $"{formattedText}\n\n" +
               $"👤 {EscapeHtml(sms.Sender)}\n" +
               $"🕒 {sentTimeFormatted}\n" +
               $"📥 {receivedTimeFormatted}";
    }

    private static string HighlightNumbers(string text)
    {
        // Highlight sequences of 4+ digits with monospace font
        // Regex: \b\d{4,}\b matches word-bounded digit sequences (4 or more)
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\b(\d{4,})\b",
            "<code>$1</code>"
        );
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
