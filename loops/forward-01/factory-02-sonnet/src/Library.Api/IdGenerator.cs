namespace Library.Api;

/// <summary>
/// ID generation policy (K-ID-001, INV-5).
/// Prefix: bk_ / mb_ / ln_
/// Suffix: GUID without hyphens (lowercase hex, 32 chars).
/// CHEAT-F01-001: chose GUID (no hyphens) as suffix for uniqueness guarantee.
/// </summary>
public static class IdGenerator
{
    public static string NewBookId()    => "bk_" + Guid.NewGuid().ToString("n");
    public static string NewMemberId()  => "mb_" + Guid.NewGuid().ToString("n");
    public static string NewLoanId()    => "ln_" + Guid.NewGuid().ToString("n");
}
