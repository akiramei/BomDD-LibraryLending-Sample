using System.Globalization;

namespace Library.Core;

/// <summary>
/// Strict UTC ISO-8601 literal-Z policy (K-UTC-ISO8601-001 / spec §1).
/// Accept: yyyy-MM-ddTHH:mm:ssZ and fractional seconds 1..7 digits, uppercase Z only.
/// Reject (=> invalid): lowercase z, numeric offset (+09:00, +00:00), no offset, date-only.
/// Output: yyyy-MM-ddTHH:mm:ssZ (second precision, no fractional; sub-second truncated).
/// </summary>
public static class UtcInstant
{
    // Accepted input formats: seconds, then 1..7 fractional-second digits. Uppercase Z literal.
    private static readonly string[] AcceptedFormats =
    {
        "yyyy-MM-ddTHH:mm:ss'Z'",
        "yyyy-MM-ddTHH:mm:ss.f'Z'",
        "yyyy-MM-ddTHH:mm:ss.ff'Z'",
        "yyyy-MM-ddTHH:mm:ss.fff'Z'",
        "yyyy-MM-ddTHH:mm:ss.ffff'Z'",
        "yyyy-MM-ddTHH:mm:ss.fffff'Z'",
        "yyyy-MM-ddTHH:mm:ss.ffffff'Z'",
        "yyyy-MM-ddTHH:mm:ss.fffffff'Z'",
    };

    public const string OutputFormat = "yyyy-MM-ddTHH:mm:ss'Z'";

    /// <summary>
    /// Parse a strict literal-Z instant. Returns true and a UTC DateTimeOffset truncated to
    /// whole seconds on success. The literal 'Z' is fixed-text matched, so only uppercase Z is
    /// accepted and any numeric offset is rejected (ParseExact does not consume trailing chars).
    /// </summary>
    public static bool TryParse(string? input, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrEmpty(input))
            return false;

        // DateTimeStyles.None: do not allow leading/trailing whitespace or implicit conversions.
        // AssumeUniversal so the parsed (Z) time is treated as UTC with zero offset.
        if (!DateTimeOffset.TryParseExact(
                input,
                AcceptedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal | DateTimeStyles.None,
                out DateTimeOffset parsed))
        {
            return false;
        }

        // Truncate sub-second; domain never uses fractional seconds.
        DateTimeOffset truncated = new DateTimeOffset(
            parsed.Year, parsed.Month, parsed.Day,
            parsed.Hour, parsed.Minute, parsed.Second,
            TimeSpan.Zero);
        value = truncated;
        return true;
    }

    /// <summary>Format an instant as yyyy-MM-ddTHH:mm:ssZ (UTC, second precision).</summary>
    public static string Format(DateTimeOffset instant)
        => instant.ToUniversalTime().ToString(OutputFormat, CultureInfo.InvariantCulture);

    /// <summary>Format a due date as yyyy-MM-dd (10 chars).</summary>
    public static string FormatDate(DateOnly date)
        => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
