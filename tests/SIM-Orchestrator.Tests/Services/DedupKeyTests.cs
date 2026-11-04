using SIMOrchestrator.Models;
using SIMOrchestrator.Services;
using Xunit;

namespace SIMOrchestrator.Tests.Services;

public class DedupKeyTests
{
    private static SmsRequest Request(string sender = "+1", string text = "hi", string? timestamp = "ts", string? messageId = null) =>
        new() { Sender = sender, Text = text, Timestamp = timestamp, MessageId = messageId };

    [Fact]
    public void Compute_IsStableForEqualContent()
    {
        Assert.Equal(DedupKey.Compute(Request()), DedupKey.Compute(Request()));
    }

    [Fact]
    public void Compute_ChangesWhenAnyFieldChanges()
    {
        var baseline = DedupKey.Compute(Request());

        Assert.NotEqual(baseline, DedupKey.Compute(Request(sender: "+2")));
        Assert.NotEqual(baseline, DedupKey.Compute(Request(text: "bye")));
        Assert.NotEqual(baseline, DedupKey.Compute(Request(timestamp: "other")));
        Assert.NotEqual(baseline, DedupKey.Compute(Request(timestamp: null)));
    }

    [Fact]
    public void Compute_PrefersDeviceMessageId()
    {
        var key = DedupKey.Compute(Request(text: "a", messageId: " abc-1 "));

        Assert.Equal("id:abc-1", key);
        Assert.Equal(key, DedupKey.Compute(Request(text: "completely different", messageId: "abc-1")));
    }

    [Fact]
    public void Compute_FitsStorageColumn()
    {
        Assert.True(DedupKey.Compute(Request()).Length <= SmsMessage.MaxDedupKeyLength);
        Assert.True(DedupKey.Compute(Request(messageId: new string('m', SmsRequest.MaxMessageIdLength))).Length <= SmsMessage.MaxDedupKeyLength);
    }
}
