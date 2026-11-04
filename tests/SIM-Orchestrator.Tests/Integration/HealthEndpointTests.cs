using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SIMOrchestrator.Tests.TestFixtures;
using Xunit;

namespace SIMOrchestrator.Tests.Integration;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_WhenCheckFails_Returns503WithoutInternalDetails()
    {
        const string secret = "Data Source=/data/secret.db; password=hunter2";
        using var factory = new SimOrchestratorFactory
        {
            ConfigureTestServices = services => services.AddHealthChecks()
                .AddCheck("broken", () => HealthCheckResult.Unhealthy(secret, new InvalidOperationException(secret)))
        };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("secret.db", raw);
        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal("unhealthy", body.GetProperty("status").GetString());
        Assert.Equal("unhealthy", body.GetProperty("checks").GetProperty("broken").GetString());
    }

    [Fact]
    public async Task Health_IsNotRateLimited()
    {
        using var factory = new SimOrchestratorFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.7");

        for (var i = 0; i < 70; i++)
        {
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Health_WithPermanentlyFailedMessages_IsDegraded()
    {
        using var factory = new SimOrchestratorFactory();
        var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SIMOrchestrator.Data.AppDbContext>();
            db.SmsMessages.Add(new SIMOrchestrator.Models.SmsMessage
            {
                Sender = "+1", Text = "gave up", DedupKey = "failed-1", ReceivedAt = DateTime.UtcNow,
                Status = SIMOrchestrator.Models.DeliveryStatus.Failed, Attempts = 20
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("degraded", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("failedMessages").GetInt32());
    }
}
