using System.Globalization;

namespace Library.Api;

/// <summary>
/// Strict UTC literal-Z ISO-8601 parser (K-UTC-ISO8601-001).
/// Accepts:  yyyy-MM-ddTHH:mm:ssZ  and  yyyy-MM-ddTHH:mm:ss.fZ ... .fffffffZ
/// Rejects:  lowercase z, numeric offsets (+09:00, +00:00), no offset, date-only.
/// </summary>
public static class DateTimeParser
{
    // All accepted formats end with literal uppercase Z
    private static readonly string[] Formats =
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
    /// Parse input strictly. Returns null if invalid.
    /// </summary>
    public static DateTime? TryParse(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        // Must end with uppercase Z
        if (!input.EndsWith('Z'))
            return null;

        if (DateTime.TryParseExact(
                input,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Format DateTime as UTC literal-Z string (no fractional seconds).
    /// CHEAT-F01-003: chose to normalize to no fractional seconds.
    /// </summary>
    public static string Format(DateTime dt)
    {
        // Ensure UTC
        var utc = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
