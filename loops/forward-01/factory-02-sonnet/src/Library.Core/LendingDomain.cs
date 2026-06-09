namespace Library.Core;

/// <summary>
/// Pure domain logic for Library Lending.
/// All methods are stateless and deterministic given inputs.
/// No HTTP, no SQLite dependencies.
/// </summary>
public static class LendingDomain
{
    // ──────────────────────────────────────────────────────────
    // Due date calculation (E-DUE-FINE-001, CP-CORE-DUE-001)
    // dueDateUtc = UTC calendar date of loanedAtUtc + 14 calendar days
    // ──────────────────────────────────────────────────────────
    public static DateOnly CalculateDueDate(DateTime loanedAtUtc)
    {
        // loanedAtUtc must be in UTC (Kind == Utc or Unspecified treated as UTC)
        var loanDate = DateOnly.FromDateTime(loanedAtUtc);
        return loanDate.AddDays(14);
    }

    // ──────────────────────────────────────────────────────────
    // Fine calculation (E-DUE-FINE-001, CP-CORE-FINE-001)
    // fineAmount = max(0, (UTC calendar day of returnedAtUtc) - dueDateUtc) * 100
    // ──────────────────────────────────────────────────────────
    public static int CalculateFine(DateTime returnedAtUtc, DateOnly dueDateUtc)
    {
        var returnDate = DateOnly.FromDateTime(returnedAtUtc);
        var daysLate = returnDate.DayNumber - dueDateUtc.DayNumber;
        return Math.Max(0, daysLate) * 100;
    }

    // ──────────────────────────────────────────────────────────
    // Overdue check (E-OVERDUE-BLOCK-001, CP-CORE-OVERDUE-001)
    // A member is overdue if ANY of their active loans has
    // UTC calendar day of newLoanedAtUtc > dueDateUtc  (strict >, not >=)
    // ──────────────────────────────────────────────────────────
    public static bool IsMemberOverdue(IEnumerable<Loan> activeLoans, DateTime newLoanedAtUtc)
    {
        var newLoanDate = DateOnly.FromDateTime(newLoanedAtUtc);
        return activeLoans.Any(loan => newLoanDate > loan.DueDateUtc);
    }

    // ──────────────────────────────────────────────────────────
    // Loan eligibility check (spec §2.4 judgment order)
    // Returns null on success, DomainError on failure.
    // Caller provides pre-fetched data (book, member, activeLoans).
    // ──────────────────────────────────────────────────────────
    public static DomainError? CheckLoanEligibility(
        Book? book,
        Member? member,
        IReadOnlyList<Loan> memberActiveLoans,
        int availableCopies,
        DateTime loanedAtUtc)
    {
        // Step 2: book or member not found
        if (book == null || member == null)
            return new DomainError.NotFound("Book or member not found.");

        // Step 3: member overdue blocked
        if (IsMemberOverdue(memberActiveLoans, loanedAtUtc))
            return new DomainError.MemberOverdueBlocked();

        // Step 4: loan limit exceeded (active <= 3; 4th is refused)
        if (memberActiveLoans.Count >= 3)
            return new DomainError.LoanLimitExceeded();

        // Step 5: no copies available
        if (availableCopies <= 0)
            return new DomainError.NoCopiesAvailable();

        return null;
    }

    // ──────────────────────────────────────────────────────────
    // Return eligibility check (spec §2.5 judgment order steps 3-4)
    // Caller validates input format (step 1) and loan existence (step 2).
    // ──────────────────────────────────────────────────────────
    public static DomainError? CheckReturnEligibility(Loan loan, DateTime returnedAtUtc)
    {
        // Step 3: already returned
        if (loan.Status == LoanStatus.Returned)
            return new DomainError.AlreadyReturned();

        // Step 4: returnedAtUtc < loanedAtUtc (instant comparison)
        if (returnedAtUtc < loan.LoanedAtUtc)
            return new DomainError.InvalidRequest("returnedAtUtc must not be before loanedAtUtc.");

        return null;
    }
}
