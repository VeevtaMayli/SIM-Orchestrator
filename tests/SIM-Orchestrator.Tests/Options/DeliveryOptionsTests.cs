using SIMOrchestrator.Options;
using Xunit;

namespace SIMOrchestrator.Tests.Options;

public class DeliveryOptionsTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(5, 60)]
    [InlineData(19, 60)]
    public void GetBackoff_FollowsScheduleAndRepeatsLastValue(int attempts, int expectedMinutes)
    {
        var options = new DeliveryOptions();

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), options.GetBackoff(attempts));
    }

    [Fact]
    public void GetBackoff_EmptySchedule_FallsBackToPollInterval()
    {
        var options = new DeliveryOptions { BackoffMinutes = [], PollInterval = TimeSpan.FromSeconds(7) };

        Assert.Equal(TimeSpan.FromSeconds(7), options.GetBackoff(3));
    }
}
