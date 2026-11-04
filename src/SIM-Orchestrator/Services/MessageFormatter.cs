using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SIMOrchestrator.Models;
using SIMOrchestrator.Options;

namespace SIMOrchestrator.Services;

/// <summary>
/// Turns a stored SMS into one or more Telegram HTML messages: escaped text with
/// digit sequences in monospace (OTP codes), sender and both timestamps in the
/// configured display time zone, split into numbered parts when too long.
/// </summary>
public sealed partial class MessageFormatter
{
    /// <summary>Hard limit of the Telegram Bot API for one text message.</summary>
    public const int MaxMessageLength = 4096;

    private const int InitialChunkLength = 3500;
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string TimeWithZoneFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private readonly TimeZoneInfo _timeZone;

    public MessageFormatter(IOptions<DisplayOptions> options)
        : this(options.Value.ResolveTimeZone())
    {
    }

    public MessageFormatter(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone;
    }

    public IReadOnlyList<string> Format(SmsMessage sms)
    {
        var footer = FormatFooter(sms);

        var single = HighlightNumbers(EscapeHtml(sms.Text)) + footer;
        if (single.Length <= MaxMessageLength)
            return [single];

        // Split the raw text, then escape/highlight each piece so markup is never cut.
        // Shrink the raw chunk size until every formatted part fits.
        for (var chunkLength = InitialChunkLength; chunkLength >= 1; chunkLength /= 2)
        {
            var chunks = SplitRaw(sms.Text, chunkLength);
            var parts = new List<string>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                parts.Add($"{HighlightNumbers(EscapeHtml(chunks[i]))}{footer}\n📄 {i + 1}/{chunks.Count}");
            }

            if (parts.All(p => p.Length <= MaxMessageLength))
                return parts;
        }

        throw new InvalidOperationException("Unable to split the message into Telegram-sized parts");
    }

    private string FormatFooter(SmsMessage sms)
    {
        return "\n\n" +
               $"👤 {EscapeHtml(sms.Sender)}\n" +
               $"🕒 {FormatSentTime(sms)}\n" +
               $"📥 {FormatInDisplayZone(AsUtc(sms.ReceivedAt))}";
    }

    /// <summary>
    /// The device timestamp: rendered in the display zone when it carries an offset,
    /// shown as sent (without a zone) when it does not, and left verbatim when it
    /// cannot be parsed. Falls back to the server receive time when absent.
    /// </summary>
    private string FormatSentTime(SmsMessage sms)
    {
        if (string.IsNullOrWhiteSpace(sms.Timestamp))
            return FormatInDisplayZone(AsUtc(sms.ReceivedAt));

        var parsed = ParseTimestamp(sms.Timestamp);
        return parsed switch
        {
            { HasOffset: true } => FormatInDisplayZone(parsed.Value.Value),
            { HasOffset: false } => parsed.Value.Value.ToString(TimeFormat, CultureInfo.InvariantCulture),
            _ => EscapeHtml(sms.Timestamp)
        };
    }

    private string FormatInDisplayZone(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(value, _timeZone).ToString(TimeWithZoneFormat, CultureInfo.InvariantCulture);

    public readonly record struct ParsedTimestamp(DateTimeOffset Value, bool HasOffset);

    /// <summary>
    /// Accepts the modem format <c>yy/MM/dd,HH:mm:ss±zz</c> (zz in quarter hours, as in
    /// AT+CMGL), ISO 8601 / RFC 3339 and the plain <c>yyyy-MM-dd HH:mm:ss</c> the firmware sends.
    /// </summary>
    public static ParsedTimestamp? ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
            return null;

        var value = timestamp.Trim();

        var modem = ModemTimestamp().Match(value);
        if (modem.Success)
        {
            var quarterHours = int.Parse(modem.Groups["tz"].Value, CultureInfo.InvariantCulture);
            if (DateTime.TryParseExact(
                    modem.Groups["dt"].Value, "yy/MM/dd,HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var local)
                && Math.Abs(quarterHours) <= 56)
            {
                var offset = TimeSpan.FromMinutes(quarterHours * 15);
                return new ParsedTimestamp(new DateTimeOffset(local, offset), HasOffset: true);
            }
            return null;
        }

        var hasOffset = TrailingOffset().IsMatch(value);
        var styles = hasOffset ? DateTimeStyles.None : DateTimeStyles.AssumeUniversal;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out var parsed))
            return new ParsedTimestamp(parsed, hasOffset);

        return null;
    }

    [GeneratedRegex(@"^(?<dt>\d{2}/\d{2}/\d{2},\d{2}:\d{2}:\d{2})(?<tz>[+-]\d{2})$")]
    private static partial Regex ModemTimestamp();

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingOffset();

    [GeneratedRegex(@"(?<!\d)(\d{4,})(?!\d)")]
    private static partial Regex DigitRun();

    /// <summary>ReceivedAt is stored in UTC; defensively treat Unspecified as UTC too.</summary>
    private static DateTimeOffset AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
        DateTimeKind.Local => new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero)
    };

    private static List<string> SplitRaw(string text, int chunkLength)
    {
        var chunks = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var length = Math.Min(chunkLength, text.Length - index);
            // Never cut a surrogate pair in half.
            if (length > 1 && char.IsHighSurrogate(text[index + length - 1]))
                length--;
            chunks.Add(text.Substring(index, length));
            index += length;
        }
        return chunks;
    }

    /// <summary>Wraps runs of four or more digits (OTP codes, amounts) in monospace so they are easy to copy.</summary>
    public static string HighlightNumbers(string escapedText) => DigitRun().Replace(escapedText, "<code>$1</code>");

    public static string EscapeHtml(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
