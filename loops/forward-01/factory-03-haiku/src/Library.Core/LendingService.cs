namespace Library.Core;

/// <summary>
/// ドメイン判定の核(M-CORE-LENDING-001)
/// HTTP・SQLite に依存しない純粋な判定ロジック
/// </summary>
public class LendingService
{
    /// <summary>
    /// 貸出可否判定(INV-1, INV-2, INV-3の順序で評価)
    /// §2.4 判定順序に従う: 1.入力検証 2.存在確認 3.延滞確認 4.上限確認 5.在庫確認
    /// </summary>
    public LoanValidationResult ValidateLoan(
        string bookId,
        string memberId,
        DateTime loanedAtUtc,
        int bookCopies,
        int activeLoanCountForBook,
        int activeLoanCountForMember,
        IEnumerable<(DateOnly dueDate, bool isActive)> memberLoans)
    {
        // 1. 入力検証は呼び出し側で行われる前提(HTTP layer 担当)

        // 2. bookId / memberId 存在確認は呼び出し側で行われる前提(HTTP layer 担当)

        // 3. 延滞判定(INV-3): memberLoans の中にいずれか1件でも延滞があるかチェック
        // 基準日時: loanedAtUtc の UTC 暦日
        var baseDateUtc = DateOnly.FromDateTime(loanedAtUtc);
        var isOverdue = memberLoans
            .Where(x => x.isActive)
            .Any(x => baseDateUtc > x.dueDate);

        if (isOverdue)
        {
            return new LoanValidationResult
            {
                IsValid = false,
                ErrorCode = "member_overdue_blocked",
                ErrorMessage = "Member has overdue loans"
            };
        }

        // 4. 会員の active loan 数 <= 3(INV-2)
        if (activeLoanCountForMember >= 3)
        {
            return new LoanValidationResult
            {
                IsValid = false,
                ErrorCode = "loan_limit_exceeded",
                ErrorMessage = "Member has reached the maximum number of active loans"
            };
        }

        // 5. 蔵書の availableCopies > 0(INV-1)
        var availableCopies = bookCopies - activeLoanCountForBook;
        if (availableCopies <= 0)
        {
            return new LoanValidationResult
            {
                IsValid = false,
                ErrorCode = "no_copies_available",
                ErrorMessage = "No copies available for this book"
            };
        }

        return new LoanValidationResult { IsValid = true };
    }

    /// <summary>
    /// 期限日計算(E-DUE-FINE-001)
    /// 暦日加算で 14 日を加える(時刻は無視)
    /// </summary>
    public DateOnly CalculateDueDate(DateTime loanedAtUtc)
    {
        var baseDate = DateOnly.FromDateTime(loanedAtUtc);
        return baseDate.AddDays(14);
    }

    /// <summary>
    /// 延滞料金計算(E-DUE-FINE-001)
    /// fineAmount = max(0, 暦日差(returnedAtUtc - dueDateUtc)) × 100
    /// </summary>
    public int CalculateFine(DateTime returnedAtUtc, DateOnly dueDateUtc)
    {
        var returnDate = DateOnly.FromDateTime(returnedAtUtc);
        var daysDifference = (returnDate.DayNumber - dueDateUtc.DayNumber);
        var fineAmount = Math.Max(0, daysDifference) * 100;
        return fineAmount;
    }

    /// <summary>
    /// 返却検証(§2.5 判定順序に従う)
    /// 1. 入力検証(HTTP layer 担当)
    /// 2. 貸出不在 → 404
    /// 3. 返却済み → 409
    /// 4. returnedAtUtc < loanedAtUtc(瞬時比較) → 400
    /// </summary>
    public ReturnValidationResult ValidateReturn(
        DateTime loanedAtUtc,
        DateTime returnedAtUtc,
        string loanStatus)
    {
        // 2. 貸出不在は呼び出し側で行われる前提

        // 3. 返却済み(status=returned)
        if (loanStatus == "returned")
        {
            return new ReturnValidationResult
            {
                IsValid = false,
                ErrorCode = "already_returned",
                ErrorMessage = "This loan has already been returned"
            };
        }

        // 4. returnedAtUtc < loanedAtUtc(瞬時比較)
        if (returnedAtUtc < loanedAtUtc)
        {
            return new ReturnValidationResult
            {
                IsValid = false,
                ErrorCode = "invalid_request",
                ErrorMessage = "Return time cannot be before loan time"
            };
        }

        return new ReturnValidationResult { IsValid = true };
    }

    /// <summary>
    /// 延滞判定(INV-3): UTC 暦日でチェック
    /// "延滞中" = その会員の active loan のいずれか1件でも UTC暦日(基準時刻) > dueDateUtc
    /// </summary>
    public bool IsOverdue(DateOnly baseDate, DateOnly dueDate)
    {
        return baseDate > dueDate;
    }
}
