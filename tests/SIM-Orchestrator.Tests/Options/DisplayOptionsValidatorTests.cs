using SIMOrchestrator.Options;
using Xunit;

namespace SIMOrchestrator.Tests.Options;

public class DisplayOptionsValidatorTests
{
    private readonly DisplayOptionsValidator _validator = new();

    [Theory]
    [InlineData("UTC")]
    [InlineData("utc")]
    [InlineData("")]
    [InlineData("Europe/Moscow")]
    public void Validate_KnownZone_Succeeds(string zone)
    {
        Assert.True(_validator.Validate(null, new DisplayOptions { TimeZone = zone }).Succeeded);
    }

    [Fact]
    public void Validate_UnknownZone_Fails()
    {
        var result = _validator.Validate(null, new DisplayOptions { TimeZone = "Mars/Olympus" });

        Assert.True(result.Failed);
        Assert.Contains("Mars/Olympus", result.FailureMessage);
    }
}
