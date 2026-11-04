using SIMOrchestrator.Models;
using SIMOrchestrator.Services;
using Xunit;

namespace SIMOrchestrator.Tests.Services;

public class MessageFormatterTests
{
    private static readonly DateTime ReceivedUtc = new(2025, 12, 28, 11, 35, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    private static SmsMessage Sms(string text = "hi", string? timestamp = null, DateTime? receivedAt = null) => new()
    {
        Id = 1,
        Sender = "+79991234567",
        Text = text,
        Timestamp = timestamp,
        ReceivedAt = receivedAt ?? ReceivedUtc
    };

    private static string Single(MessageFormatter formatter, SmsMessage sms) => Assert.Single(formatter.Format(sms));

    [Fact]
    public void Format_RendersTextSenderAndTimesInUtcByDefault()
    {
        var message = Single(new MessageFormatter(TimeZoneInfo.Utc), Sms("Your code 123456", "2025-12-28T14:30:00+03:00"));

        Assert.Equal(
            "Your code <code>123456</code>\n\n" +
            "👤 +79991234567\n" +
            "🕒 2025-12-28 11:30:00 +00:00\n" +
            "📥 2025-12-28 11:35:00 +00:00",
            message);
    }

    [Fact]
    public void Format_UsesConfiguredDisplayTimeZone()
    {
        var message = Single(new MessageFormatter(Moscow), Sms("x", "2025-12-28T14:30:00+03:00"));

        Assert.Contains("🕒 2025-12-28 14:30:00 +03:00", message);
        Assert.Contains("📥 2025-12-28 14:35:00 +03:00", message);
    }

    [Fact]
    public void Format_ModemTimestamp_QuarterHourOffsetIsConverted()
    {
        // AT+CMGL style: +12 quarter hours = +03:00
        var message = Single(new MessageFormatter(TimeZoneInfo.Utc), Sms("x", "25/12/28,14:30:00+12"));

        Assert.Contains("🕒 2025-12-28 11:30:00 +00:00", message);
    }

    [Fact]
    public void Format_TimestampWithoutZone_IsShownAsSentWithoutOffset()
    {
        var message = Single(new MessageFormatter(Moscow), Sms("x", "2025-12-28 14:30:00"));

        Assert.Contains("🕒 2025-12-28 14:30:00\n", message);
        Assert.DoesNotContain("🕒 2025-12-28 14:30:00 +", message);
    }

    [Fact]
    public void Format_MissingTimestamp_FallsBackToReceivedAt()
    {
        var message = Single(new MessageFormatter(TimeZoneInfo.Utc), Sms("x", timestamp: "  "));

        Assert.Contains("🕒 2025-12-28 11:35:00 +00:00", message);
    }

    [Fact]
    public void Format_UnparsableTimestamp_IsShownVerbatimAndEscaped()
    {
        var message = Single(new MessageFormatter(TimeZoneInfo.Utc), Sms("x", "<garbage>"));

        Assert.Contains("🕒 &lt;garbage&gt;", message);
    }

    [Fact]
    public void Format_ReceivedAtWithUnspecifiedKind_IsTreatedAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(ReceivedUtc, DateTimeKind.Unspecified);

        var message = Single(new MessageFormatter(Moscow), Sms("x", receivedAt: unspecified));

        Assert.Contains("📥 2025-12-28 14:35:00 +03:00", message);
    }

    [Fact]
    public void Format_EscapesHtmlInTextAndSender()
    {
        var sms = Sms("<b>a & b</b>");
        sms.Sender = "<Bank>";

        var message = Single(new MessageFormatter(TimeZoneInfo.Utc), sms);

        Assert.StartsWith("&lt;b&gt;a &amp; b&lt;/b&gt;", message);
        Assert.Contains("👤 &lt;Bank&gt;", message);
    }

    [Theory]
    [InlineData("code 1234", "code <code>1234</code>")]
    [InlineData("code 123", "code 123")]
    [InlineData("abc1234", "abc<code>1234</code>")]
    [InlineData("12345678901", "<code>12345678901</code>")]
    [InlineData("1234-5678", "<code>1234</code>-<code>5678</code>")]
    public void HighlightNumbers_WrapsRunsOfFourOrMoreDigits(string input, string expected)
    {
        Assert.Equal(expected, MessageFormatter.HighlightNumbers(input));
    }

    [Theory]
    [InlineData("25/12/28,14:30:00+12", 2025, 12, 28, 14, 30, 180, true)]
    [InlineData("25/12/28,14:30:00-20", 2025, 12, 28, 14, 30, -300, true)]
    [InlineData("2025-12-28T14:30:00+03:00", 2025, 12, 28, 14, 30, 180, true)]
    [InlineData("2025-12-28T11:30:00Z", 2025, 12, 28, 11, 30, 0, true)]
    [InlineData("2025-12-28 14:30:00", 2025, 12, 28, 14, 30, 0, false)]
    public void ParseTimestamp_RecognisesSupportedFormats(string input, int y, int mo, int d, int h, int mi, int offsetMinutes, bool hasOffset)
    {
        var parsed = MessageFormatter.ParseTimestamp(input);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), parsed.Value.Value.DateTime);
        Assert.Equal(TimeSpan.FromMinutes(offsetMinutes), parsed.Value.Value.Offset);
        Assert.Equal(hasOffset, parsed.Value.HasOffset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("25/13/28,14:30:00+12")]
    [InlineData("25/12/28,14:30:00+99")]
    public void ParseTimestamp_RejectsGarbage(string? input)
    {
        Assert.Null(MessageFormatter.ParseTimestamp(input));
    }

    [Fact]
    public void Format_LongText_SplitsIntoNumberedPartsThatEachFit()
    {
        var text = string.Concat(Enumerable.Range(0, 250).Select(i => $"code {100000 + i} & "));
        var parts = new MessageFormatter(TimeZoneInfo.Utc).Format(Sms(text));

        Assert.True(parts.Count >= 2);
        Assert.All(parts, p => Assert.True(p.Length <= MessageFormatter.MaxMessageLength));
        for (var i = 0; i < parts.Count; i++)
        {
            Assert.EndsWith($"📄 {i + 1}/{parts.Count}", parts[i]);
            Assert.Contains("👤 +79991234567", parts[i]);
        }
        var reassembled = string.Concat(parts.Select(p => p[..p.IndexOf("\n\n👤", StringComparison.Ordinal)]));
        Assert.Equal(MessageFormatter.HighlightNumbers(MessageFormatter.EscapeHtml(text)), reassembled);
    }

    [Fact]
    public void Format_LongText_NeverSplitsSurrogatePairs()
    {
        // One leading char shifts every pair by one, so a naive cut at the chunk
        // boundary would land between a high and a low surrogate.
        var text = "a" + string.Concat(Enumerable.Repeat("😀", 2500));
        var parts = new MessageFormatter(TimeZoneInfo.Utc).Format(Sms(text));

        Assert.True(parts.Count >= 2);
        foreach (var part in parts)
        {
            var body = part[..part.IndexOf("\n\n👤", StringComparison.Ordinal)];
            Assert.False(char.IsHighSurrogate(body[^1]), "part ends with a lone high surrogate");
            Assert.False(char.IsLowSurrogate(body[0]), "part starts with a lone low surrogate");
        }
        var reassembled = string.Concat(parts.Select(p => p[..p.IndexOf("\n\n👤", StringComparison.Ordinal)]));
        Assert.Equal(text, reassembled);
    }

    [Fact]
    public void Format_ReceivedAtWithLocalKind_IsConvertedToUtcFirst()
    {
        var local = DateTime.SpecifyKind(ReceivedUtc, DateTimeKind.Utc).ToLocalTime();
        Assert.Equal(DateTimeKind.Local, local.Kind);

        var message = Single(new MessageFormatter(TimeZoneInfo.Utc), Sms("x", receivedAt: local));

        Assert.Contains("📥 2025-12-28 11:35:00 +00:00", message);
    }
}
