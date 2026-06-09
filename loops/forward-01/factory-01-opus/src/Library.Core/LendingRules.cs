namespace Library.Core;

/// <summary>
/// ドメイン判定エラーの語彙(仕様 §2.8 のうちドメイン層が生み出すもの)。
/// 入力検証(invalid_request)・不在(not_found)は表面層(API)が担う。
/// </summary>
public enum LendingError
{
    None,
    NoCopiesAvailable,     // no_copies_available (INV-1)
    LoanLimitExceeded,     // loan_limit_exceeded (INV-2)
    MemberOverdueBlocked   // member_overdue_blocked (INV-3)
}

/// <summary>
/// 純粋なドメイン判定。HTTP・SQLite に依存しない。日時は呼び出し側から受け取る。
/// 期限・延滞・料金は UTC 暦日演算(整数演算のみ)。判定順序は仕様 §2.4/§2.5 の通り。
/// </summary>
public static class LendingRules
{
    public const int LoanPeriodDays = 14;
    public const int LoanLimit = 3;
    public const int FinePerDay = 100;

    /// <summary>
    /// 瞬時(DateTimeOffset)を UTC 暦日(DateOnly)へ。サーバ時計は使わない。
    /// </summary>
    public static DateOnly UtcCalendarDate(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.UtcDateTime);

    /// <summary>
    /// dueDateUtc = UTC暦日(loanedAtUtc) + 14日(暦日加算。時刻は使わない)。
    /// </summary>
    public static DateOnly ComputeDueDate(DateTimeOffset loanedAtUtc)
        => UtcCalendarDate(loanedAtUtc).AddDays(LoanPeriodDays);

    /// <summary>
    /// fineAmount = max(0, 暦日差(UTC暦日(returnedAtUtc) − dueDateUtc)) × 100。
    /// 期限日当日(時刻不問)= 0 / 翌日 = 100 / 早期返却 = 0。
    /// </summary>
    public static int ComputeFine(DateTimeOffset returnedAtUtc, DateOnly dueDateUtc)
    {
        int dayDiff = UtcCalendarDate(returnedAtUtc).DayNumber - dueDateUtc.DayNumber;
        return Math.Max(0, dayDiff) * FinePerDay;
    }

    /// <summary>
    /// 会員が「延滞中」か。INV-3: その会員の active loan の いずれか1件でも
    /// UTC暦日(基準時刻) > dueDateUtc。基準時刻は新規貸出の loanedAtUtc。
    /// 期限日当日はまだ延滞でない(> のみ、≥ ではない)。returned は数えない。
    /// </summary>
    public static bool IsMemberOverdue(IEnumerable<Loan> memberActiveLoans, DateTimeOffset referenceInstant)
    {
        DateOnly referenceDate = UtcCalendarDate(referenceInstant);
        foreach (var loan in memberActiveLoans)
        {
            if (loan.Status != LoanStatus.Active)
                continue;
            if (referenceDate.DayNumber > loan.DueDateUtc.DayNumber)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 貸出可否のドメイン判定(仕様 §2.4 の判定3→4→5の順)。
    /// 判定1(入力検証)・判定2(不在)は表面層で済んでいる前提で呼ぶ。
    /// </summary>
    /// <param name="bookCopies">対象蔵書の総部数</param>
    /// <param name="bookActiveLoanCount">対象蔵書の現在の active loan 数</param>
    /// <param name="memberActiveLoans">対象会員の active loan 群(延滞判定用)</param>
    /// <param name="loanedAtUtc">新規貸出の瞬時(延滞判定の基準時刻)</param>
    public static LendingError EvaluateLoan(
        int bookCopies,
        int bookActiveLoanCount,
        IReadOnlyCollection<Loan> memberActiveLoans,
        DateTimeOffset loanedAtUtc)
    {
        // 3. 会員が延滞中 → member_overdue_blocked
        if (IsMemberOverdue(memberActiveLoans, loanedAtUtc))
            return LendingError.MemberOverdueBlocked;

        // 4. 会員の active loan が 3 件 → loan_limit_exceeded
        if (memberActiveLoans.Count(l => l.Status == LoanStatus.Active) >= LoanLimit)
            return LendingError.LoanLimitExceeded;

        // 5. availableCopies == 0 → no_copies_available
        if (bookCopies - bookActiveLoanCount <= 0)
            return LendingError.NoCopiesAvailable;

        return LendingError.None;
    }

    /// <summary>
    /// availableCopies = copies − active loan 数(常時)。
    /// </summary>
    public static int AvailableCopies(int copies, int activeLoanCount)
        => copies - activeLoanCount;
}
