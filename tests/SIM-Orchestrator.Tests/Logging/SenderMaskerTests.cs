using SIMOrchestrator.Logging;
using Xunit;

namespace SIMOrchestrator.Tests.Logging;

public class SenderMaskerTests
{
    [Theory]
    [InlineData("+79991234567", "********4567")]
    [InlineData("79991234567", "*******4567")]
    [InlineData("12345", "*2345")]
    public void Mask_PhoneNumber_KeepsOnlyLastFourDigits(string input, string expected)
    {
        Assert.Equal(expected, SenderMasker.Mask(input));
    }

    [Theory]
    [InlineData("Sberbank")]
    [InlineData("900")]
    [InlineData("MTS-Info")]
    public void Mask_AlphanumericOrShortSender_IsUnchanged(string input)
    {
        Assert.Equal(input, SenderMasker.Mask(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Mask_Empty_ReturnsPlaceholder(string? input)
    {
        Assert.Equal("<empty>", SenderMasker.Mask(input));
    }
}
