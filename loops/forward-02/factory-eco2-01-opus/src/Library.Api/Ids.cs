namespace Library.Api;

/// <summary>ID = prefix + 32-digit lowercase hex (GUID "N"). INV-5 / K-ID-001 v2. Server-generated only.</summary>
public static class Ids
{
    public static string Book()   => "bk_" + Guid.NewGuid().ToString("N");
    public static string Member() => "mb_" + Guid.NewGuid().ToString("N");
    public static string Loan()   => "ln_" + Guid.NewGuid().ToString("N");
}
