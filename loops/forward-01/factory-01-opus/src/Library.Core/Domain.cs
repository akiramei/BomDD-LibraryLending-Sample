namespace Library.Core;

/// <summary>
/// 貸出の状態。INV-4: active -> returned の一方向のみ。
/// </summary>
public enum LoanStatus
{
    Active,
    Returned
}

/// <summary>
/// 蔵書。copies は総部数。
/// </summary>
public sealed record Book(string Id, string Title, int Copies);

/// <summary>
/// 会員。
/// </summary>
public sealed record Member(string Id, string Name);

/// <summary>
/// 貸出。日時は瞬時(DateTimeOffset, UTC)で保持し、暦日判定は DateOnly で行う。
/// dueDate は暦日(yyyy-MM-dd)。
/// </summary>
public sealed record Loan(
    string Id,
    string BookId,
    string MemberId,
    DateTimeOffset LoanedAtUtc,
    DateOnly DueDateUtc,
    LoanStatus Status,
    DateTimeOffset? ReturnedAtUtc,
    int? FineAmount);
