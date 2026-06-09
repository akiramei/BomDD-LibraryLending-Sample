using System;
using System.Globalization;

namespace Library.Api;

/// <summary>
/// Strict ISO-8601 UTC literal-Z parser (K-UTC-ISO8601-001 / E-DATETIME-POLICY-001).
/// Accepts:
///   yyyy-MM-ddTHH:mm:ssZ
///   yyyy-MM-ddTHH:mm:ss.fZ  (1-7 fractional digits)
/// Rejects everything else (lowercase z, numeric offsets including +00:00, no offset, date-only).
/// Output: DateTimeOffset with Kind=Utc, sub-second truncated (rev2).
/// </summary>
public static class DateTimeParser
{
    private static readonly string[] AcceptedFormats =
    [
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fZ",
        "yyyy-MM-ddTHH:mm:ss.ffZ",
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-ddTHH:mm:ss.ffffZ",
        "yyyy-MM-ddTHH:mm:ss.fffffZ",
        "yyyy-MM-ddTHH:mm:ss.ffffffZ",
        "yyyy-MM-ddTHH:mm:ss.fffffffZ",
    ];

    /// <summary>
    /// Tries to parse the input string.
    /// Returns true with a DateTimeOffset (sub-second truncated) on success.
    /// Returns false if the format is invalid.
    /// </summary>
    public static bool TryParse(string? input, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrEmpty(input))
            return false;

        // The string must end with uppercase 'Z' — reject lowercase z and offsets.
        if (!input.EndsWith('Z'))
            return false;

        // Try each accepted format (exact match, no style flags that allow extra leniency).
        DateTimeOffset parsed;
        bool ok = DateTimeOffset.TryParseExact(
            input,
            AcceptedFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed);

        if (!ok)
            return false;

        // Truncate sub-second (rev2: output is second-precision).
        result = new DateTimeOffset(
            parsed.Year, parsed.Month, parsed.Day,
            parsed.Hour, parsed.Minute, parsed.Second,
            TimeSpan.Zero);
        return true;
    }

    /// <summary>Formats a DateTimeOffset as yyyy-MM-ddTHH:mm:ssZ (rev2 output format).</summary>
    public static string Format(DateTimeOffset dt)
        => dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
