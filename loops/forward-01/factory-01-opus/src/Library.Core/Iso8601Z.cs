using System.Globalization;

namespace Library.Core;

/// <summary>
/// strict literal-Z 日時ポリシー(K-UTC-ISO8601-001)。
/// 受理: yyyy-MM-ddTHH:mm:ssZ および小数秒 1〜7 桁付き。大文字 Z のみ。
/// 拒否: 小文字 z / 数値オフセット(+09:00, +00:00 含む)/ オフセット無し / 日付のみ。
/// 既定の DateTime(Offset).Parse はオフセットを受理してしまうため、明示フォーマットで検証する。
/// 未来日時は受理(サーバ時計は使わない)。
/// </summary>
public static class Iso8601Z
{
    // 秒まで(小数秒なし)+ 小数秒 1〜7 桁。すべて末尾は大文字 Z。
    private static readonly string[] AcceptedFormats =
    {
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fZ",
        "yyyy-MM-ddTHH:mm:ss.ffZ",
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-ddTHH:mm:ss.ffffZ",
        "yyyy-MM-ddTHH:mm:ss.fffffZ",
        "yyyy-MM-ddTHH:mm:ss.ffffffZ",
        "yyyy-MM-ddTHH:mm:ss.fffffffZ",
    };

    /// <summary>
    /// strict literal-Z をパースする。成功時 true、結果は UTC の DateTimeOffset。
    /// </summary>
    public static bool TryParse(string? input, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrEmpty(input))
            return false;

        // 末尾が大文字 'Z' でなければ即拒否(小文字 z・オフセット・日付のみを弾く)。
        if (input[^1] != 'Z')
            return false;

        // フォーマット中の 'Z' はリテラル一致(AssumeUniversal で UTC とみなす)。
        // 余分な空白は許可しない(NoCurrentDateDefault は無関係なので AssumeUniversal のみ)。
        if (DateTimeOffset.TryParseExact(
                input,
                AcceptedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            result = parsed.ToUniversalTime();
            return true;
        }

        return false;
    }
}
