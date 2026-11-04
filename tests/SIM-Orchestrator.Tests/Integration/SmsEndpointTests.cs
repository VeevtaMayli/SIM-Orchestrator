using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SIMOrchestrator.Models;
using SIMOrchestrator.Tests.TestFixtures;
using Xunit;

namespace SIMOrchestrator.Tests.Integration;

public class SmsEndpointTests : IClassFixture<SimOrchestratorFactory>
{
    private readonly SimOrchestratorFactory _factory;

    public SmsEndpointTests(SimOrchestratorFactory factory)
    {
        _factory = factory;
    }

    private static HttpRequestMessage Post(object body, string? apiKey, string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/sms")
        {
            Content = JsonContent.Create(body)
        };
        if (apiKey is not null)
            request.Headers.Add("X-API-Key", apiKey);
        request.Headers.Add("X-Forwarded-For", clientIp);
        return request;
    }

    private static object ValidSms(string text = "Your code is 123456") =>
        new { sender = "+79991234567", text, timestamp = "2025-12-28 14:30:00" };

    private async Task WaitForDeliveryAsync(string text)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (_factory.Telegram.Sent.Any(s => s.Text == text))
                return;
            await Task.Delay(25);
        }
        Assert.Fail($"'{text}' was not delivered to Telegram within 5 seconds");
    }

    [Fact]
    public async Task Post_ValidRequest_StoresAndDeliversInBackground()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Post(ValidSms("hello-store"), SimOrchestratorFactory.ApiKey, "10.0.0.1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("received", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
        await WaitForDeliveryAsync("hello-store");
    }

    [Fact]
    public async Task Post_SameSmsTwice_ReturnsOriginalIdAndDeliversOnce()
    {
        var client = _factory.CreateClient();
        var sms = ValidSms("retry-from-device");

        var first = await client.SendAsync(Post(sms, SimOrchestratorFactory.ApiKey, "10.0.0.7"));
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        await WaitForDeliveryAsync("retry-from-device");

        var second = await client.SendAsync(Post(sms, SimOrchestratorFactory.ApiKey, "10.0.0.7"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate", secondBody.GetProperty("status").GetString());
        Assert.Equal(firstBody.GetProperty("id").GetInt32(), secondBody.GetProperty("id").GetInt32());
        await Task.Delay(200);
        Assert.Single(_factory.Telegram.Sent, s => s.Text == "retry-from-device");
    }

    [Fact]
    public async Task Post_WithDeviceMessageId_DeduplicatesOnThatId()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid().ToString("N");

        var first = await client.SendAsync(Post(new { sender = "+1", text = "v1", messageId = id }, SimOrchestratorFactory.ApiKey, "10.0.0.8"));
        var second = await client.SendAsync(Post(new { sender = "+1", text = "v2-reencoded", messageId = id }, SimOrchestratorFactory.ApiKey, "10.0.0.8"));

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate", secondBody.GetProperty("status").GetString());
        Assert.Equal(firstBody.GetProperty("id").GetInt32(), secondBody.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Post_WithoutApiKey_Returns401WithChallenge()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Post(ValidSms(), apiKey: null, "10.0.0.2"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEmpty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task Post_WithWrongApiKey_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Post(ValidSms(), "definitely-not-the-configured-key-123", "10.0.0.3"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_TextTooLong_Returns400()
    {
        var client = _factory.CreateClient();
        var tooLong = new string('a', SmsRequest.MaxTextLength + 1);

        var response = await client.SendAsync(Post(ValidSms(tooLong), SimOrchestratorFactory.ApiKey, "10.0.0.4"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_EmptyText_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(Post(new { sender = "+1", text = "" }, SimOrchestratorFactory.ApiKey, "10.0.0.5"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_PhoneNumber_IsMaskedInLogs()
    {
        var client = _factory.CreateClient();
        const string sender = "+79165550001";

        var response = await client.SendAsync(Post(new { sender, text = "mask-me" }, SimOrchestratorFactory.ApiKey, "10.0.0.6"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var relevant = _factory.Logs.Entries.Where(e => e.Message.Contains("mask-me") || e.Message.Contains("0001")).ToList();
        Assert.NotEmpty(relevant);
        Assert.All(_factory.Logs.Entries, e => Assert.DoesNotContain(sender, e.FullText));
    }

    [Fact]
    public async Task RateLimit_IsAppliedPerForwardedClientIp()
    {
        var client = _factory.CreateClient();
        const string limitedIp = "203.0.113.10";

        for (var i = 0; i < 60; i++)
        {
            var ok = await client.SendAsync(Post(ValidSms($"burst-{i}"), SimOrchestratorFactory.ApiKey, limitedIp));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var rejected = await client.SendAsync(Post(ValidSms("burst-61"), SimOrchestratorFactory.ApiKey, limitedIp));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        var otherClient = await client.SendAsync(Post(ValidSms("other-ip"), SimOrchestratorFactory.ApiKey, "203.0.113.11"));
        Assert.Equal(HttpStatusCode.OK, otherClient.StatusCode);
    }

    [Fact]
    public async Task Health_WithoutApiKey_ReportsHealthyWithBacklog()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
        Assert.Equal("connected", body.GetProperty("database").GetString());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("unsentMessages").ValueKind);
        Assert.Equal(JsonValueKind.Number, body.GetProperty("failedMessages").ValueKind);
    }

    [Fact]
    public async Task Swagger_InDevelopment_IsReachableWithoutApiKey()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
