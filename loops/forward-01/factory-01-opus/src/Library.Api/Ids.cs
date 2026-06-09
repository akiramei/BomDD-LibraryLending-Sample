namespace Library.Api;

/// <summary>
/// ID 採番(K-ID-001 / INV-5)。接頭辞は固定(bk_/mb_/ln_)。
/// 接頭辞以降は実装裁量 = ずる報告対象(CHEAT-F01-001)。
/// 採用形式: 接頭辞 + GUID("N" 形式の 32 桁 hex 小文字)。
/// 理由: サーバ生成のみで一意・追加パッケージ不要(BCL の Guid)。
/// 序数比較(§2.6)は hex 文字列で安定して定まる。
/// </summary>
public static class Ids
{
    public static string NewBookId() => "bk_" + Guid.NewGuid().ToString("N");
    public static string NewMemberId() => "mb_" + Guid.NewGuid().ToString("N");
    public static string NewLoanId() => "ln_" + Guid.NewGuid().ToString("N");
}
