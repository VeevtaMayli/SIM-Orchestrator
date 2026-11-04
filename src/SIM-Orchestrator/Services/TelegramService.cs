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
    private const int MaxLoggedResponseLength = 500;
    private const int MaxErrorLength = 200;

    private readonly HttpClient _httpClient;
    private readonly MessageFormatter _formatter;
    private readonly ILogger<TelegramService> _logger;
    private readonly string _chatId;

    /// <param name="httpClient">
    /// Must have a BaseAddress that already contains the bot token
    /// (see <see cref="TelegramServiceCollectionExtensions.AddTelegramClient"/>).
    /// </param>
    public TelegramService(
        HttpClient httpClient,
        IOptions<TelegramOptions> options,
        MessageFormatter formatter,
        ILogger<TelegramService> logger)
    {
        _httpClient = httpClient;
        _formatter = formatter;
        _logger = logger;
        _chatId = options.Value.ChatId;
    }

    public async Task<TelegramSendResult> SendSmsToTelegramAsync(SmsMessage sms, CancellationToken cancellationToken = default)
    {
        var parts = _formatter.Format(sms);
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

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";
}
