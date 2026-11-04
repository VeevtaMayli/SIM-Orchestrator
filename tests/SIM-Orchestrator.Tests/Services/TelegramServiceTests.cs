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

        Assert.True(result);
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

        Assert.False(result);
        var warning = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("BadRequest", warning.Message);
        Assert.True(warning.Message.Length < 1000, "error body must be truncated");
        Assert.All(logs.Entries, entry => Assert.DoesNotContain(BotToken, entry.FullText));
    }

    [Fact]
    public async Task Send_TransportFailure_ReturnsFalseWithoutLeakingToken()
    {
        var (service, logs, _) = Build(_ => throw new HttpRequestException("connection refused"));

        var result = await service.SendSmsToTelegramAsync(Sms());

        Assert.False(result);
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
}
