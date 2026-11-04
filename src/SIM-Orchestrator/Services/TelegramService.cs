using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SIMOrchestrator.Models;
using SIMOrchestrator.Options;

namespace SIMOrchestrator.Services;

public class TelegramService : ITelegramService
{
    private const int MaxLoggedResponseLength = 500;

    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramService> _logger;
    private readonly string _chatId;

    /// <param name="httpClient">
    /// Must have a BaseAddress that already contains the bot token
    /// (see <see cref="TelegramServiceCollectionExtensions.AddTelegramClient"/>).
    /// </param>
    public TelegramService(
        HttpClient httpClient,
        IOptions<TelegramOptions> options,
        ILogger<TelegramService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _chatId = options.Value.ChatId;
    }

    public async Task<bool> SendSmsToTelegramAsync(SmsMessage sms)
    {
        try
        {
            var message = FormatMessage(sms);

            var payload = new
            {
                chat_id = _chatId,
                text = message,
                parse_mode = "HTML"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending SMS to Telegram. ID: {Id}", sms.Id);

            var response = await _httpClient.PostAsync("sendMessage", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("SMS sent to Telegram. ID: {Id}", sms.Id);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Telegram API error. Status: {Status}, Response: {Response}",
                response.StatusCode, Truncate(errorContent, MaxLoggedResponseLength));
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
        // Regex: (?<!\d)\d{4,}(?!\d) matches digit sequences (4 or more) not surrounded by other digits
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(?<!\d)(\d{4,})(?!\d)",
            "<code>$1</code>"
        );
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
