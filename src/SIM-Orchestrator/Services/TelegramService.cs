using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SIMOrchestrator.Models;
using SIMOrchestrator.Options;

namespace SIMOrchestrator.Services;

public class TelegramService : ITelegramService
{
    /// <summary>Hard limit of the Telegram Bot API for one text message.</summary>
    public const int MaxMessageLength = 4096;

    private const int MaxLoggedResponseLength = 500;
    private const int MaxErrorLength = 200;
    private const int InitialChunkLength = 3500;

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

    public async Task<TelegramSendResult> SendSmsToTelegramAsync(SmsMessage sms, CancellationToken cancellationToken = default)
    {
        var parts = FormatMessages(sms);
        _logger.LogInformation("Sending SMS to Telegram. ID: {Id}, Parts: {Parts}", sms.Id, parts.Count);

        foreach (var part in parts)
        {
            var result = await SendTextAsync(part, cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to send SMS to Telegram. ID: {Id}, Error: {Error}", sms.Id, result.Error);
                return result;
            }
        }

        _logger.LogInformation("SMS sent to Telegram. ID: {Id}", sms.Id);
        return TelegramSendResult.Ok();
    }

    private async Task<TelegramSendResult> SendTextAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                chat_id = _chatId,
                text,
                parse_mode = "HTML"
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync("sendMessage", content, cancellationToken);
            if (response.IsSuccessStatusCode)
                return TelegramSendResult.Ok();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telegram API error. Status: {Status}, Response: {Response}",
                response.StatusCode, Truncate(body, MaxLoggedResponseLength));

            var retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests
                ? ParseRetryAfter(body, response.Headers.RetryAfter)
                : null;

            return TelegramSendResult.Failure(
                $"Telegram API {(int)response.StatusCode}: {Truncate(body, MaxErrorLength)}",
                retryAfter);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Includes the HttpClient timeout (TaskCanceledException without our token being cancelled).
            _logger.LogError(ex, "Failed to send message to Telegram");
            return TelegramSendResult.Failure($"{ex.GetType().Name}: {Truncate(ex.Message, MaxErrorLength)}");
        }
    }

    /// <summary>Reads <c>parameters.retry_after</c> from a 429 body, falling back to the Retry-After header.</summary>
    internal static TimeSpan? ParseRetryAfter(string body, RetryConditionHeaderValue? header)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("parameters", out var parameters)
                && parameters.TryGetProperty("retry_after", out var retryAfter)
                && retryAfter.TryGetInt32(out var seconds)
                && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through to the header.
        }

        if (header?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        return null;
    }

    /// <summary>
    /// Formats the SMS as one or more Telegram messages, each within the API limit.
    /// </summary>
    internal static IReadOnlyList<string> FormatMessages(SmsMessage sms)
    {
        var footer = FormatFooter(sms);

        var single = HighlightNumbers(EscapeHtml(sms.Text)) + footer;
        if (single.Length <= MaxMessageLength)
            return [single];

        // Split the raw text, then escape/highlight each piece so markup is never cut.
        // Shrink the raw chunk size until every formatted part fits.
        for (var chunkLength = InitialChunkLength; chunkLength >= 1; chunkLength /= 2)
        {
            var chunks = SplitRaw(sms.Text, chunkLength);
            var parts = new List<string>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                parts.Add($"{HighlightNumbers(EscapeHtml(chunks[i]))}{footer}\n📄 {i + 1}/{chunks.Count}");
            }

            if (parts.All(p => p.Length <= MaxMessageLength))
                return parts;
        }

        throw new InvalidOperationException("Unable to split the message into Telegram-sized parts");
    }

    private static List<string> SplitRaw(string text, int chunkLength)
    {
        var chunks = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var length = Math.Min(chunkLength, text.Length - index);
            // Never cut a surrogate pair in half.
            if (length > 1 && char.IsHighSurrogate(text[index + length - 1]))
                length--;
            chunks.Add(text.Substring(index, length));
            index += length;
        }
        return chunks;
    }

    private static string FormatFooter(SmsMessage sms)
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
                ? ToUtc(sms.ReceivedAt).ToString("yyyy-MM-dd HH:mm:ss zzz")
                : sms.Timestamp;
        }

        var receivedTimeFormatted = ToUtc(sms.ReceivedAt).ToString("yyyy-MM-dd HH:mm:ss zzz");

        return "\n\n" +
               $"👤 {EscapeHtml(sms.Sender)}\n" +
               $"🕒 {sentTimeFormatted}\n" +
               $"📥 {receivedTimeFormatted}";
    }

    /// <summary>ReceivedAt is always UTC; values read from SQLite may carry Kind=Unspecified.</summary>
    private static DateTimeOffset ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(value, TimeSpan.Zero)
            : new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);

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
