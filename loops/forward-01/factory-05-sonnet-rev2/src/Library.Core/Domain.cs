using System;

namespace Library.Core;

// ── Domain value objects ──────────────────────────────────────────────────

/// <summary>Result of the loan-eligibility check (§2.4 判定順序).</summary>
public enum LoanCheckResult
{
    Ok,
    MemberOverdueBlocked,   // 409 member_overdue_blocked
    LoanLimitExceeded,      // 409 loan_limit_exceeded
    NoCopiesAvailable,      // 409 no_copies_available
}

// ── Pure domain calculations ──────────────────────────────────────────────

/// <summary>
/// All business logic that is free of HTTP / SQLite concerns.
/// Dates are DateOnly (UTC calendar day).  Times are DateTimeOffset (UTC instant).
/// </summary>
public static class LendingDomain
{
    // ── dueDate calculation ──────────────────────────────────────────────

    /// <summary>
    /// dueDateUtc = UTC-calendar-day of loanedAtUtc + 14 days (CP-CORE-DUE-001).
    /// </summary>
    public static DateOnly ComputeDueDate(DateTimeOffset loanedAtUtc)
    {
        var day = DateOnly.FromDateTime(loanedAtUtc.UtcDateTime);
        return day.AddDays(14);
    }

    // ── fine calculation ─────────────────────────────────────────────────

    /// <summary>
    /// fineAmount = max(0, UTC-calendar-day(returnedAtUtc) − dueDateUtc) × 100 (CP-CORE-FINE-001).
    /// </summary>
    public static int ComputeFine(DateTimeOffset returnedAtUtc, DateOnly dueDateUtc)
    {
        var returnDay = DateOnly.FromDateTime(returnedAtUtc.UtcDateTime);
        int diff = returnDay.DayNumber - dueDateUtc.DayNumber;
        return Math.Max(0, diff) * 100;
    }

    // ── overdue check ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if any active loan is overdue relative to the reference instant (CP-CORE-OVERDUE-001).
    /// Overdue = UTC-calendar-day(referenceUtc) > dueDateUtc  (strictly greater, INV-3).
    /// </summary>
    public static bool IsOverdue(DateTimeOffset referenceUtc, DateOnly dueDateUtc)
    {
        var refDay = DateOnly.FromDateTime(referenceUtc.UtcDateTime);
        return refDay > dueDateUtc;
    }

    // ── loan eligibility check (§2.4 判定順序 3→4→5) ─────────────────────

    /// <summary>
    /// Check loan eligibility after input validation and entity existence checks (steps 1 and 2
    /// are handled by the API layer).  Evaluates steps 3, 4, 5 in order.
    /// </summary>
    /// <param name="loanedAtUtc">The instant carried by the request (server clock not used).</param>
    /// <param name="memberActiveLoanDueDates">Due dates of all active loans of the member.</param>
    /// <param name="memberActiveLoanCount">Total active loan count for the member.</param>
    /// <param name="bookAvailableCopies">Current available copies of the book.</param>
    public static LoanCheckResult CheckLoanEligibility(
        DateTimeOffset loanedAtUtc,
        IEnumerable<DateOnly> memberActiveLoanDueDates,
        int memberActiveLoanCount,
        int bookAvailableCopies)
    {
        // Step 3: overdue block (any active loan overdue)
        foreach (var due in memberActiveLoanDueDates)
        {
            if (IsOverdue(loanedAtUtc, due))
                return LoanCheckResult.MemberOverdueBlocked;
        }

        // Step 4: loan limit
        if (memberActiveLoanCount >= 3)
            return LoanCheckResult.LoanLimitExceeded;

        // Step 5: copies available
        if (bookAvailableCopies <= 0)
            return LoanCheckResult.NoCopiesAvailable;

        return LoanCheckResult.Ok;
    }
}
