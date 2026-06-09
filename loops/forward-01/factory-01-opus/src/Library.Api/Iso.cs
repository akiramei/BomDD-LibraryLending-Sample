using System.Globalization;

namespace Library.Api;

/// <summary>
/// 応答・保存用の ISO-8601 literal-Z 整形(K-UTC-ISO8601-001)。
/// 応答日時は小数秒なしの "yyyy-MM-ddTHH:mm:ssZ"(秒精度に正規化 = ずる報告 CHEAT-F01-002)。
/// echo は仕様で要求されない(§2.4)。同一瞬時を表す literal-Z であればよい。
/// dueDate は "yyyy-MM-dd" の 10 文字。
/// </summary>
public static class Iso
{
    public static string Format(DateTimeOffset instant)
        => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly date)
        => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // 保存値の読み戻し(秒精度の literal-Z を瞬時へ)。
    public static DateTimeOffset Parse(string s)
        => DateTimeOffset.ParseExact(s, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static DateOnly ParseDate(string s)
        => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
