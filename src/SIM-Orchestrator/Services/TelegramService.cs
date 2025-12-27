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
        var timestamp = string.IsNullOrEmpty(sms.Timestamp)
            ? sms.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss")
            : sms.Timestamp;

        return $"<b>SMS Received</b>\n\n" +
               $"<b>From:</b> {EscapeHtml(sms.Sender)}\n" +
               $"<b>Time:</b> {timestamp}\n\n" +
               $"{EscapeHtml(sms.Text)}";
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
