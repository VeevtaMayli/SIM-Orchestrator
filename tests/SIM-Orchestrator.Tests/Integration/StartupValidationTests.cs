using SIMOrchestrator.Tests.TestFixtures;
using Xunit;

namespace SIMOrchestrator.Tests.Integration;

public class StartupValidationTests
{
    [Fact]
    public void MissingBotToken_PreventsStartup()
    {
        using var factory = new SimOrchestratorFactory();
        factory.Settings["Telegram:BotToken"] = null;

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("BotToken", ex.ToString());
    }

    [Fact]
    public void ShortApiKey_PreventsStartup()
    {
        using var factory = new SimOrchestratorFactory();
        factory.Settings["ApiKey"] = "short";

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("32", ex.ToString());
    }

    [Fact]
    public void KeyListWithoutLegacyKey_Starts()
    {
        using var factory = new SimOrchestratorFactory();
        factory.Settings["ApiKey"] = null;
        factory.Settings["ApiKeys:0"] = new string('r', 40);

        var client = factory.CreateClient();

        Assert.NotNull(client);
    }
}
