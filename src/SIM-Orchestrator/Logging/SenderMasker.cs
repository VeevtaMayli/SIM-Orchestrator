using System.Text.RegularExpressions;

namespace SIMOrchestrator.Logging;

/// <summary>
/// Masks phone numbers before they reach logs. Alphanumeric sender ids
/// (bank or service names) are not personal data and are kept as-is.
/// </summary>
public static partial class SenderMasker
{
    private const int VisibleDigits = 4;

    [GeneratedRegex(@"^\+?\d{5,}$")]
    private static partial Regex PhoneNumber();

    public static string Mask(string? sender)
    {
        if (string.IsNullOrEmpty(sender))
            return "<empty>";

        if (!PhoneNumber().IsMatch(sender))
            return sender;

        var masked = new string('*', sender.Length - VisibleDigits);
        return masked + sender[^VisibleDigits..];
    }
}
