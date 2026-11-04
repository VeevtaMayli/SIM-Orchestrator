using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIMOrchestrator.Models;
using SIMOrchestrator.Services;
using SIMOrchestrator.Tests.TestFixtures;
using Xunit;

namespace SIMOrchestrator.Tests.Services;

/// <summary>
/// Exercises TelegramService through the real DI registration so that the
/// HttpClientFactory pipeline (and its logging) is part of what is tested.
/// </summary>
public class TelegramServiceTests
{
    private const string BotToken = "987654321:SECRET-bot-token-value";

    private static (ITelegramService Service, CapturingLoggerProvider Logs, StubHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var logs = new CapturingLoggerProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = BotToken,
                ["Telegram:ChatId"] = "42"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging =>
        {
            logging.AddProvider(logs);
            logging.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddTelegramClient().ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ITelegramService>(), logs, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static SmsMessage Sms(string text = "Code 1234") => new()
    {
        Id = 7,
        Sender = "+79991234567",
        Text = text,
        ReceivedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Send_Success_PostsToTokenUrlAndReturnsTrue()
    {
        var (service, _, handler) = Build(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));

        var result = await service.SendSmsToTelegramAsync(Sms());

        Assert.True(result.Success);
        var uri = Assert.Single(handler.RequestUris);
        Assert.Equal($"https://api.telegram.org/bot{BotToken}/sendMessage", uri);
        Assert.Contains("\"chat_id\":\"42\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Send_NeverWritesBotTokenToLogs()
    {
        var (service, logs, _) = Build(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));

        await service.SendSmsToTelegramAsync(Sms());

        Assert.NotEmpty(logs.Entries);
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(BotToken, entry.FullText));
    }

    [Fact]
    public async Task Send_ApiError_ReturnsFalseAndLogsTruncatedBodyWithoutToken()
    {
        var hugeBody = new string('x', 5000);
        var (service, logs, _) = Build(_ => Json(HttpStatusCode.BadRequest, hugeBody));

        var result = await service.SendSmsToTelegramAsync(Sms());

        Assert.False(result.Success);
        Assert.Null(result.RetryAfter);
        Assert.Contains("400", result.Error);
        Assert.True(result.Error!.Length < 300, "error summary must be short");
        var warning = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Telegram API error"));
        Assert.Contains("BadRequest", warning.Message);
        Assert.True(warning.Message.Length < 1000, "error body must be truncated");
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(BotToken, entry.FullText));
    }

    [Fact]
    public async Task Send_TransportFailure_ReturnsFalseWithoutLeakingToken()
    {
        var (service, logs, _) = Build(_ => throw new HttpRequestException("connection refused"));

        var result = await service.SendSmsToTelegramAsync(Sms());

        Assert.False(result.Success);
        Assert.Contains("HttpRequestException", result.Error);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(BotToken, entry.FullText));
    }

    [Fact]
    public async Task Send_EscapesHtmlAndHighlightsCodes()
    {
        var (service, _, handler) = Build(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));

        await service.SendSmsToTelegramAsync(Sms("<b>Your code 123456 & more</b>"));

        var payload = System.Text.Json.JsonDocument.Parse(handler.RequestBodies[0]);
        var text = payload.RootElement.GetProperty("text").GetString()!;
        Assert.Contains("&lt;b&gt;", text);
        Assert.Contains("&amp;", text);
        Assert.Contains("<code>123456</code>", text);
        Assert.Equal("HTML", payload.RootElement.GetProperty("parse_mode").GetString());
    }

    [Fact]
    public async Task Send_TooManyRequests_ReturnsRetryAfterFromBody()
    {
        var (service, _, _) = Build(_ => Json(
            HttpStatusCode.TooManyRequests,
            """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 37","parameters":{"retry_after":37}}"""));

        var result = await service.SendSmsToTelegramAsync(Sms());

        Assert.False(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(37), result.RetryAfter);
    }

    [Fact]
    public async Task Send_TooManyRequestsWithoutBody_FallsBackToRetryAfterHeader()
    {
        var (service, _, _) = Build(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("slow down") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return response;
        });

        var result = await service.SendSmsToTelegramAsync(Sms());

        Assert.Equal(TimeSpan.FromSeconds(12), result.RetryAfter);
    }

    [Fact]
    public async Task Send_ClientTimeout_ReturnsFailureWithoutToken()
    {
        var (service, logs, _) = Build(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing."));

        var result = await service.SendSmsToTelegramAsync(Sms());

        Assert.False(result.Success);
        Assert.Contains("TaskCanceledException", result.Error);
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(BotToken, entry.FullText));
    }

    [Fact]
    public async Task Send_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (service, _, _) = Build(_ => throw new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendSmsToTelegramAsync(Sms(), cts.Token));
    }

    [Fact]
    public async Task Send_LongText_IsSplitIntoNumberedPartsWithinLimit()
    {
        var (service, _, handler) = Build(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));
        var text = string.Concat(Enumerable.Range(0, 250).Select(i => $"code {100000 + i} & "));
        Assert.True(text.Length <= SmsRequest.MaxTextLength);

        var result = await service.SendSmsToTelegramAsync(Sms(text));

        Assert.True(result.Success);
        Assert.True(handler.RequestBodies.Count >= 2);
        var parts = handler.RequestBodies
            .Select(b => System.Text.Json.JsonDocument.Parse(b).RootElement.GetProperty("text").GetString()!)
            .ToList();
        Assert.All(parts, p => Assert.True(p.Length <= TelegramService.MaxMessageLength));
        Assert.All(parts, p => Assert.DoesNotContain("<cod\n", p));
        for (var i = 0; i < parts.Count; i++)
            Assert.EndsWith($"📄 {i + 1}/{parts.Count}", parts[i]);
        Assert.Equal(parts.Count, parts.Count(p => p.Contains("👤 +79991234567")));
    }

    [Fact]
    public async Task Send_LongText_StopsAtFirstFailedPart()
    {
        var calls = 0;
        var (service, _, _) = Build(_ => ++calls == 1
            ? Json(HttpStatusCode.OK, """{"ok":true}""")
            : Json(HttpStatusCode.BadGateway, "nope"));
        // Every '&' becomes '&amp;', so the formatted text is far above the limit.
        var text = new string('&', SmsRequest.MaxTextLength);

        var result = await service.SendSmsToTelegramAsync(Sms(text));

        Assert.False(result.Success);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("""{"parameters":{"retry_after":5}}""", 5)]
    [InlineData("""{"parameters":{"retry_after":0}}""", null)]
    [InlineData("not json", null)]
    [InlineData("{}", null)]
    public void ParseRetryAfter_ReadsBodyOrReturnsNull(string body, int? expectedSeconds)
    {
        var parsed = TelegramService.ParseRetryAfter(body, null);

        Assert.Equal(expectedSeconds is null ? null : TimeSpan.FromSeconds(expectedSeconds.Value), parsed);
    }
}
