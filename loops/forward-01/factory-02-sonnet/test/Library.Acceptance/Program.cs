using Library.Core;

/// <summary>
/// Self-acceptance harness for Library.Core unit tests.
/// Covers all CP-CORE-* test vectors (Control Plan unit layer).
/// Calls Library.Core directly — no process or HTTP.
/// Outputs PASS/FAIL for each test. Exits with code 1 if any FAIL.
/// </summary>

int passed = 0;
int failed = 0;

void Pass(string name)
{
    Console.WriteLine($"PASS: {name}");
    passed++;
}

void Fail(string name, string reason)
{
    Console.WriteLine($"FAIL: {name} — {reason}");
    failed++;
}

void Assert(bool condition, string name, string? failReason = null)
{
    if (condition) Pass(name);
    else Fail(name, failReason ?? "assertion failed");
}

// ═══════════════════════════════════════════════════════════════════════════════
// CP-CORE-DUE-001: dueDateUtc = UTC calendar date + 14 days
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("--- CP-CORE-DUE-001: dueDate calculation ---");

{
    // 2026-01-31 → 2026-02-14 (month boundary)
    var dt = new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc);
    var due = LendingDomain.CalculateDueDate(dt);
    Assert(due == new DateOnly(2026, 2, 14), "DUE-001-a: 2026-01-31 → 2026-02-14",
        $"got {due}");
}
{
    // 2026-12-25 → 2027-01-08 (year boundary)
    var dt = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc);
    var due = LendingDomain.CalculateDueDate(dt);
    Assert(due == new DateOnly(2027, 1, 8), "DUE-001-b: 2026-12-25 → 2027-01-08",
        $"got {due}");
}
{
    // 2026-06-10T23:59:59Z → 2026-06-24 (time irrelevant)
    var dt = new DateTime(2026, 6, 10, 23, 59, 59, DateTimeKind.Utc);
    var due = LendingDomain.CalculateDueDate(dt);
    Assert(due == new DateOnly(2026, 6, 24), "DUE-001-c: 2026-06-10T23:59:59Z → 2026-06-24",
        $"got {due}");
}

// ═══════════════════════════════════════════════════════════════════════════════
// CP-CORE-FINE-001: fineAmount = max(0, calendar days late) × 100
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("--- CP-CORE-FINE-001: fine calculation ---");

{
    // Due date: 2026-06-24
    // Return on same day at 23:59:59Z → 0  (FMEA-001 target)
    var dueDate = new DateOnly(2026, 6, 24);
    var returnAt = new DateTime(2026, 6, 24, 23, 59, 59, DateTimeKind.Utc);
    var fine = LendingDomain.CalculateFine(returnAt, dueDate);
    Assert(fine == 0, "FINE-001-a: return on due date 23:59:59Z → 0", $"got {fine}");
}
{
    // Return on next day 00:00:00Z → 100
    var dueDate = new DateOnly(2026, 6, 24);
    var returnAt = new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc);
    var fine = LendingDomain.CalculateFine(returnAt, dueDate);
    Assert(fine == 100, "FINE-001-b: return day after due 00:00:00Z → 100", $"got {fine}");
}
{
    // Return 3 days late → 300
    var dueDate = new DateOnly(2026, 6, 24);
    var returnAt = new DateTime(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);
    var fine = LendingDomain.CalculateFine(returnAt, dueDate);
    Assert(fine == 300, "FINE-001-c: 3 days late → 300", $"got {fine}");
}
{
    // Early return (before due) → 0
    var dueDate = new DateOnly(2026, 6, 24);
    var returnAt = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
    var fine = LendingDomain.CalculateFine(returnAt, dueDate);
    Assert(fine == 0, "FINE-001-d: early return → 0", $"got {fine}");
}

// ═══════════════════════════════════════════════════════════════════════════════
// CP-CORE-OVERDUE-001: overdue block (any, boundary, returned exclusion)
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("--- CP-CORE-OVERDUE-001: overdue block ---");

{
    // Loan with due date today → NOT overdue (> only, not >=)
    var today = new DateTime(2026, 6, 24, 10, 0, 0, DateTimeKind.Utc);
    var dueDateUtc = new DateOnly(2026, 6, 24);  // same calendar day
    var activeLoans = new List<Loan>
    {
        new Loan("ln_x", "bk_x", "mb_x", today.AddDays(-14), dueDateUtc, LoanStatus.Active)
    };
    var blocked = LendingDomain.IsMemberOverdue(activeLoans, today);
    Assert(!blocked, "OVERDUE-001-a: due date == today → not overdue", $"got {blocked}");
}
{
    // loanedAtUtc 1 day past due date → overdue
    var newLoanDate = new DateTime(2026, 6, 25, 9, 0, 0, DateTimeKind.Utc);
    var dueDateUtc = new DateOnly(2026, 6, 24);
    var activeLoans = new List<Loan>
    {
        new Loan("ln_x", "bk_x", "mb_x", newLoanDate.AddDays(-15), dueDateUtc, LoanStatus.Active)
    };
    var blocked = LendingDomain.IsMemberOverdue(activeLoans, newLoanDate);
    Assert(blocked, "OVERDUE-001-b: 1 day past due → overdue", $"got {blocked}");
}
{
    // After returning the overdue loan: only active loans checked
    // (simulate: no active loans)
    var newLoanDate = new DateTime(2026, 6, 25, 9, 0, 0, DateTimeKind.Utc);
    var activeLoans = new List<Loan>(); // no active loans
    var blocked = LendingDomain.IsMemberOverdue(activeLoans, newLoanDate);
    Assert(!blocked, "OVERDUE-001-c: returned overdue → no block on subsequent loan", $"got {blocked}");
}
{
    // 2 active loans, 1 is overdue → blocked (any)
    var newLoanDate = new DateTime(2026, 6, 25, 9, 0, 0, DateTimeKind.Utc);
    var activeLoans = new List<Loan>
    {
        new Loan("ln_1", "bk_1", "mb_x", newLoanDate.AddDays(-10), new DateOnly(2026, 6, 28), LoanStatus.Active), // not overdue
        new Loan("ln_2", "bk_2", "mb_x", newLoanDate.AddDays(-20), new DateOnly(2026, 6, 24), LoanStatus.Active), // overdue (due 6/24, new loan on 6/25)
    };
    var blocked = LendingDomain.IsMemberOverdue(activeLoans, newLoanDate);
    Assert(blocked, "OVERDUE-001-d: 1 of 2 active loans overdue → blocked (any)", $"got {blocked}");
}

// ═══════════════════════════════════════════════════════════════════════════════
// CP-CORE-AVAIL-001: availability (copies - active loans)
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("--- CP-CORE-AVAIL-001: availability ---");

{
    // copies=1: 1st loan ok, 2nd rejected
    var book = new Book("bk_test", "Test Book", 1);
    var member = new Member("mb_test", "Test Member");

    // 1st loan: no active loans yet
    var err1 = LendingDomain.CheckLoanEligibility(book, member, [], 1,
        new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc));
    Assert(err1 == null, "AVAIL-001-a: copies=1, first loan succeeds");

    // 2nd loan: 1 active loan exists, availableCopies=0
    var existingLoan = new Loan("ln_1", "bk_test", "mb_test",
        new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
        new DateOnly(2026, 6, 24), LoanStatus.Active);
    var err2 = LendingDomain.CheckLoanEligibility(book, member, [existingLoan], 0,
        new DateTime(2026, 6, 10, 11, 0, 0, DateTimeKind.Utc));
    Assert(err2 is DomainError.NoCopiesAvailable, "AVAIL-001-b: copies=1, second loan → no_copies_available",
        $"got {err2}");
}
{
    // copies=2: 2nd ok, 3rd rejected
    var book = new Book("bk_test2", "Test Book 2", 2);
    var member = new Member("mb_test2", "Test Member 2");
    var loan1 = new Loan("ln_a", "bk_test2", "mb_test2",
        new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
        new DateOnly(2026, 6, 24), LoanStatus.Active);
    var loan2 = new Loan("ln_b", "bk_test2", "mb_other",
        new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
        new DateOnly(2026, 6, 24), LoanStatus.Active);

    // 2nd copy: availableCopies=1, member has 0 active loans for this book
    var err1 = LendingDomain.CheckLoanEligibility(book, member, [loan1], 1,
        new DateTime(2026, 6, 10, 11, 0, 0, DateTimeKind.Utc));
    Assert(err1 == null, "AVAIL-001-c: copies=2, second loan succeeds");

    // 3rd: availableCopies=0
    var err2 = LendingDomain.CheckLoanEligibility(book, member, [loan1], 0,
        new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
    Assert(err2 is DomainError.NoCopiesAvailable, "AVAIL-001-d: copies=2, third loan → no_copies_available",
        $"got {err2}");
}

// ═══════════════════════════════════════════════════════════════════════════════
// CP-CORE-LIMIT-001: member active loan limit (≤ 3)
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("--- CP-CORE-LIMIT-001: member loan limit ---");

{
    var book = new Book("bk_lim", "Limit Book", 10);
    var member = new Member("mb_lim", "Limit Member");

    static Loan MakeLoan(int i) => new Loan($"ln_{i}", "bk_lim", "mb_lim",
        new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
        new DateOnly(2026, 6, 15), LoanStatus.Active);

    var loanDate = new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);

    // 3rd loan (2 existing) → success
    var activeLoans2 = new List<Loan> { MakeLoan(1), MakeLoan(2) };
    var err3 = LendingDomain.CheckLoanEligibility(book, member, activeLoans2, 10, loanDate);
    Assert(err3 == null, "LIMIT-001-a: 3rd loan (2 existing) → success", $"got {err3}");

    // 4th loan (3 existing) → loan_limit_exceeded
    var activeLoans3 = new List<Loan> { MakeLoan(1), MakeLoan(2), MakeLoan(3) };
    var err4 = LendingDomain.CheckLoanEligibility(book, member, activeLoans3, 10, loanDate);
    Assert(err4 is DomainError.LoanLimitExceeded, "LIMIT-001-b: 4th loan → loan_limit_exceeded",
        $"got {err4}");

    // Returned loans don't count: 3 total but 1 returned → 2 active → success
    // (simulate by passing only active loans to CheckLoanEligibility)
    var activeLoans2b = new List<Loan> { MakeLoan(1), MakeLoan(2) }; // 3rd was returned
    var errAfterReturn = LendingDomain.CheckLoanEligibility(book, member, activeLoans2b, 10, loanDate);
    Assert(errAfterReturn == null, "LIMIT-001-c: 3 loans, 1 returned → 2 active → success",
        $"got {errAfterReturn}");
}

// ═══════════════════════════════════════════════════════════════════════════════
// Additional: return eligibility (E-LOAN-STATE-001)
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("--- E-LOAN-STATE-001: return eligibility ---");

{
    var loanedAt = new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);
    var loan = new Loan("ln_r1", "bk_r1", "mb_r1", loanedAt, new DateOnly(2026, 6, 24), LoanStatus.Active);

    // Already returned → AlreadyReturned
    var returnedLoan = loan with { Status = LoanStatus.Returned };
    var err1 = LendingDomain.CheckReturnEligibility(returnedLoan,
        new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc));
    Assert(err1 is DomainError.AlreadyReturned, "STATE-001-a: already_returned", $"got {err1}");

    // returnedAt < loanedAt (instant comparison) → InvalidRequest
    var err2 = LendingDomain.CheckReturnEligibility(loan,
        new DateTime(2026, 6, 9, 10, 0, 0, DateTimeKind.Utc));
    Assert(err2 is DomainError.InvalidRequest, "STATE-001-b: returnedAt < loanedAt → invalid_request",
        $"got {err2}");

    // returnedAt == loanedAt → valid (same instant is accepted)
    var err3 = LendingDomain.CheckReturnEligibility(loan, loanedAt);
    Assert(err3 == null, "STATE-001-c: returnedAt == loanedAt → accepted", $"got {err3}");

    // Normal return → ok
    var err4 = LendingDomain.CheckReturnEligibility(loan,
        new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc));
    Assert(err4 == null, "STATE-001-d: normal return → ok", $"got {err4}");
}

// ═══════════════════════════════════════════════════════════════════════════════
// Summary
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine($"Results: {passed} PASS, {failed} FAIL");

return failed > 0 ? 1 : 0;
