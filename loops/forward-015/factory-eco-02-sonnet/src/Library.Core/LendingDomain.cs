namespace Library.Core;

/// <summary>
/// Pure domain decisions for the Library Lending product.
/// No HTTP / SQLite dependency. All time inputs are passed by the caller.
/// Date/overdue/fine arithmetic uses UTC calendar days (DateOnly). Integer money only.
/// Decision order follows spec §2.4 / §2.5 (top-down, first match wins).
/// </summary>
public static class LendingDomain
{
    public const int LoanPeriodDays = 14;
    public const int MaxActiveLoansPerMember = 3;
    public const int FinePerOverdueDay = 100;

    /// <summary>UTC calendar day of an instant.</summary>
    public static DateOnly UtcDate(DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.UtcDateTime);

    /// <summary>dueDateUtc = UTC calendar day of loanedAt + 14 days (calendar addition; time-of-day ignored).</summary>
    public static DateOnly DueDate(DateTimeOffset loanedAtUtc)
        => UtcDate(loanedAtUtc).AddDays(LoanPeriodDays);

    /// <summary>fineAmount = max(0, (UtcDate(returnedAt) - dueDate) days) * 100. Integer only.</summary>
    public static int Fine(DateTimeOffset returnedAtUtc, DateOnly dueDate)
    {
        int diff = UtcDate(returnedAtUtc).DayNumber - dueDate.DayNumber;
        return diff > 0 ? diff * FinePerOverdueDay : 0;
    }

    /// <summary>
    /// A member is "overdue" (blocking new loans) when ANY of their active loans satisfies
    /// UtcDate(referenceInstant) > dueDate (strict; same calendar day is NOT yet overdue). INV-3.
    /// referenceInstant for a new loan = that loan's loanedAtUtc.
    /// </summary>
    public static bool IsMemberOverdue(DateTimeOffset referenceInstant, IEnumerable<DateOnly> activeLoanDueDates)
    {
        DateOnly refDate = UtcDate(referenceInstant);
        foreach (DateOnly due in activeLoanDueDates)
        {
            if (refDate.DayNumber > due.DayNumber)
                return true;
        }
        return false;
    }
}

/// <summary>Result codes for the loan-eligibility decision (spec §2.4 order).</summary>
public enum LoanDecision
{
    Allowed,
    NotFound,             // bookId/memberId missing
    MemberOverdueBlocked, // 409 member_overdue_blocked
    LoanLimitExceeded,    // 409 loan_limit_exceeded
    NoCopiesAvailable     // 409 no_copies_available
}

/// <summary>State known at loan-decision time (after input validation has already passed).</summary>
public readonly record struct LoanContext(
    bool BookExists,
    bool MemberExists,
    DateTimeOffset LoanedAtUtc,
    IReadOnlyList<DateOnly> MemberActiveLoanDueDates,
    int BookAvailableCopies);

/// <summary>Result codes for the return decision (spec §2.5 order, after input validation).</summary>
public enum ReturnDecision
{
    Allowed,
    NotFound,         // loan {id} missing
    AlreadyReturned,  // 409 already_returned
    ReturnBeforeLoan  // 400 invalid_request (returnedAt < loanedAt, instant comparison)
}

public static class LendingDecisions
{
    /// <summary>
    /// Evaluates loan eligibility in spec §2.4 order. Assumes input validation (non-empty ids,
    /// valid datetime) already passed. Existence is checked before overdue/limit/copies.
    /// </summary>
    public static LoanDecision EvaluateLoan(in LoanContext ctx)
    {
        // 2. existence (both missing still => not_found)
        if (!ctx.BookExists || !ctx.MemberExists)
            return LoanDecision.NotFound;

        // 3. member overdue (any active loan overdue at this loan's loanedAt)
        if (LendingDomain.IsMemberOverdue(ctx.LoanedAtUtc, ctx.MemberActiveLoanDueDates))
            return LoanDecision.MemberOverdueBlocked;

        // 4. active loan limit (>= 3 active => 4th blocked)
        if (ctx.MemberActiveLoanDueDates.Count >= LendingDomain.MaxActiveLoansPerMember)
            return LoanDecision.LoanLimitExceeded;

        // 5. copies
        if (ctx.BookAvailableCopies == 0)
            return LoanDecision.NoCopiesAvailable;

        return LoanDecision.Allowed;
    }

    /// <summary>
    /// Evaluates return in spec §2.5 order. Assumes input validation (valid datetime) already passed.
    /// </summary>
    public static ReturnDecision EvaluateReturn(
        bool loanExists,
        bool alreadyReturned,
        DateTimeOffset loanedAtUtc,
        DateTimeOffset returnedAtUtc)
    {
        // 2. loan not found
        if (!loanExists)
            return ReturnDecision.NotFound;

        // 3. already returned
        if (alreadyReturned)
            return ReturnDecision.AlreadyReturned;

        // 4. returnedAt < loanedAt (instant comparison; same instant accepted)
        if (returnedAtUtc < loanedAtUtc)
            return ReturnDecision.ReturnBeforeLoan;

        return ReturnDecision.Allowed;
    }
}
