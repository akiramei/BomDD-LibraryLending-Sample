namespace Library.Core;

/// <summary>
/// 蔵書モデル (E-BOOK-INVENTORY-001)
/// </summary>
public class Book
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Copies { get; set; }
    public int AvailableCopies { get; set; }
}

/// <summary>
/// 会員モデル
/// </summary>
public class Member
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 貸出モデル (E-LOAN-STATE-001)
/// </summary>
public class Loan
{
    public string Id { get; set; } = string.Empty;
    public string BookId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public DateTime LoanedAtUtc { get; set; }
    public DateOnly DueDateUtc { get; set; }
    public string Status { get; set; } = "active"; // "active" or "returned"
    public DateTime? ReturnedAtUtc { get; set; }
    public int? FineAmount { get; set; }
}

/// <summary>
/// 貸出判定の結果(REQ-001, REQ-002, REQ-004)
/// </summary>
public class LoanValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 返却判定の結果(REQ-003, REQ-007)
/// </summary>
public class ReturnValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? FineAmount { get; set; }
}
