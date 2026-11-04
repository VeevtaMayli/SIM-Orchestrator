using Microsoft.Extensions.Options;

namespace SIMOrchestrator.Options;

/// <summary>How times are rendered in Telegram messages.</summary>
public sealed class DisplayOptions
{
    public const string SectionName = "Display";

    /// <summary>IANA (or Windows) time zone id used to display times, for example <c>Europe/Moscow</c>.</summary>
    public string TimeZone { get; set; } = "UTC";

    public TimeZoneInfo ResolveTimeZone() =>
        string.IsNullOrWhiteSpace(TimeZone) || TimeZone.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
}

public sealed class DisplayOptionsValidator : IValidateOptions<DisplayOptions>
{
    public ValidateOptionsResult Validate(string? name, DisplayOptions options)
    {
        try
        {
            options.ResolveTimeZone();
            return ValidateOptionsResult.Success;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return ValidateOptionsResult.Fail($"Display:TimeZone '{options.TimeZone}' is not a known time zone: {ex.Message}");
        }
    }
}
