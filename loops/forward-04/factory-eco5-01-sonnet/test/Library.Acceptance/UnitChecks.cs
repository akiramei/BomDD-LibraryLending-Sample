using Library.Core;

namespace Library.Acceptance;

/// <summary>
/// Unit-depth coverage of every CP-CORE-* test_vector by calling Library.Core directly
/// (Control Plan depth "unit": domain decisions checked with no process surface).
/// </summary>
public static class UnitChecks
{
    public static void Run(Harness h)
    {
        AvailCheck(h);      // CP-CORE-AVAIL-001
        LimitCheck(h);      // CP-CORE-LIMIT-001 (rev3: type-dependent)
        MemberTypeCheck(h); // CP-MEMBER-TYPE-001 (rev3)
        DueCheck(h);        // CP-CORE-DUE-001
        FineCheck(h);       // CP-CORE-FINE-001
        OverdueCheck(h);    // CP-CORE-OVERDUE-001
    }

    private static DateTimeOffset Inst(string z)
    {
        if (!UtcInstant.TryParse(z, out var v))
            throw new ArgumentException($"test vector not parseable: {z}");
        return v;
    }

    private static DateOnly Due(string z, MemberType type = MemberType.Standard)
        => LendingDomain.DueDate(Inst(z), type);

    // CP-CORE-AVAIL-001: copies=1 1st ok/2nd no_copies; return restores; copies=2 two ok/3rd reject.
    private static void AvailCheck(Harness h)
    {
        // copies=1: 1 active loan => available 0 => next loan rejected.
        var ctx1 = new LoanContext(true, true, Inst("2026-06-10T10:00:00Z"),
            Array.Empty<DateOnly>(), BookAvailableCopies: 1);
        h.Check("CP-CORE-AVAIL-001/copies=1 first loan allowed",
            LendingDecisions.EvaluateLoan(ctx1) == LoanDecision.Allowed);

        var ctxFull = ctx1 with { BookAvailableCopies = 0 };
        h.Check("CP-CORE-AVAIL-001/copies=1 second loan no_copies_available",
            LendingDecisions.EvaluateLoan(ctxFull) == LoanDecision.NoCopiesAvailable);

        // after return: available restored to 1 => allowed again.
        var ctxRestored = ctx1 with { BookAvailableCopies = 1 };
        h.Check("CP-CORE-AVAIL-001/after return available restored, reloan allowed",
            LendingDecisions.EvaluateLoan(ctxRestored) == LoanDecision.Allowed);

        // copies=2: 2 ok, 3rd (available 0) rejected.
        var ctx2 = ctx1 with { BookAvailableCopies = 2 };
        h.Check("CP-CORE-AVAIL-001/copies=2 has availability",
            LendingDecisions.EvaluateLoan(ctx2) == LoanDecision.Allowed);
        h.Check("CP-CORE-AVAIL-001/copies=2 third loan rejected when 0 left",
            LendingDecisions.EvaluateLoan(ctx2 with { BookAvailableCopies = 0 }) == LoanDecision.NoCopiesAvailable);
    }

    // CP-CORE-LIMIT-001 (rev3): standard 3rd ok/4th blocked; premium 5th ok/6th blocked;
    // unspecified default = standard (4th blocked); returned not counted.
    private static void LimitCheck(Harness h)
    {
        var loanInstant = Inst("2026-06-10T10:00:00Z");
        // far-future due dates so overdue does not interfere.
        var due = new DateOnly(2099, 1, 1);

        DateOnly[] Active(int n) => Enumerable.Repeat(due, n).ToArray();

        LoanContext Std(int active) =>
            new(true, true, loanInstant, Active(active), BookAvailableCopies: 10, MemberType.Standard);
        LoanContext Prem(int active) =>
            new(true, true, loanInstant, Active(active), BookAvailableCopies: 10, MemberType.Premium);

        // standard: 3rd allowed (2 active), 4th blocked (3 active).
        h.Check("CP-CORE-LIMIT-001/standard third loan allowed (boundary is 4th)",
            LendingDecisions.EvaluateLoan(Std(2)) == LoanDecision.Allowed);
        h.Check("CP-CORE-LIMIT-001/standard fourth loan loan_limit_exceeded",
            LendingDecisions.EvaluateLoan(Std(3)) == LoanDecision.LoanLimitExceeded);

        // premium (rev3): 5th allowed (4 active), 6th blocked (5 active).
        h.Check("CP-CORE-LIMIT-001/premium fifth loan allowed (boundary is 6th)",
            LendingDecisions.EvaluateLoan(Prem(4)) == LoanDecision.Allowed);
        h.Check("CP-CORE-LIMIT-001/premium sixth loan loan_limit_exceeded",
            LendingDecisions.EvaluateLoan(Prem(5)) == LoanDecision.LoanLimitExceeded);

        // unspecified default = standard: 4 active members blocked at 4th (LoanContext default type).
        var defaultType = new LoanContext(true, true, loanInstant, Active(3), BookAvailableCopies: 10);
        h.Check("CP-CORE-LIMIT-001/default (standard) fourth loan loan_limit_exceeded",
            LendingDecisions.EvaluateLoan(defaultType) == LoanDecision.LoanLimitExceeded);

        // returned loans are not part of active due-date list => back under limit.
        h.Check("CP-CORE-LIMIT-001/standard returned not counted, new loan allowed",
            LendingDecisions.EvaluateLoan(Std(2)) == LoanDecision.Allowed);
    }

    // CP-MEMBER-TYPE-001 (rev3): "premium" parses; unspecified defaults to standard; "gold" rejected.
    private static void MemberTypeCheck(Harness h)
    {
        h.Check("CP-MEMBER-TYPE-001/\"premium\" parses to Premium",
            MemberTypes.TryParse("premium", out var p) && p == MemberType.Premium);
        h.Check("CP-MEMBER-TYPE-001/unspecified (null) defaults to standard",
            MemberTypes.TryParse(null, out var d) && d == MemberType.Standard);
        h.Check("CP-MEMBER-TYPE-001/\"standard\" parses to Standard",
            MemberTypes.TryParse("standard", out var s) && s == MemberType.Standard);
        h.Check("CP-MEMBER-TYPE-001/\"gold\" rejected (invalid_request)",
            !MemberTypes.TryParse("gold", out _));
        // limit lookup wiring: premium=5 / standard=3.
        h.Check("CP-MEMBER-TYPE-001/limit standard=3",
            LendingDomain.LoanLimit(MemberType.Standard) == 3);
        h.Check("CP-MEMBER-TYPE-001/limit premium=5",
            LendingDomain.LoanLimit(MemberType.Premium) == 5);
    }

    // CP-CORE-DUE-001: standard +14 / premium +21 (rev4), month/year rollover, time irrelevant.
    private static void DueCheck(Harness h)
    {
        // standard = +14 days.
        h.Check("CP-CORE-DUE-001/standard 2026-01-31 -> 2026-02-14 (month rollover)",
            UtcInstant.FormatDate(Due("2026-01-31T10:00:00Z")) == "2026-02-14");
        h.Check("CP-CORE-DUE-001/standard 2026-12-25 -> 2027-01-08 (year rollover)",
            UtcInstant.FormatDate(Due("2026-12-25T00:00:00Z")) == "2027-01-08");
        h.Check("CP-CORE-DUE-001/standard 2026-06-10T23:59:59Z -> 2026-06-24 (time irrelevant)",
            UtcInstant.FormatDate(Due("2026-06-10T23:59:59Z")) == "2026-06-24");

        // premium = +21 days (rev4/ECO-002).
        h.Check("CP-CORE-DUE-001/premium 2026-01-31 -> 2026-02-21 (month rollover)",
            UtcInstant.FormatDate(Due("2026-01-31T10:00:00Z", MemberType.Premium)) == "2026-02-21");
        h.Check("CP-CORE-DUE-001/premium 2026-12-25 -> 2027-01-15 (year rollover)",
            UtcInstant.FormatDate(Due("2026-12-25T00:00:00Z", MemberType.Premium)) == "2027-01-15");
        h.Check("CP-CORE-DUE-001/premium 2026-06-10T23:59:59Z -> 2026-07-01 (time irrelevant)",
            UtcInstant.FormatDate(Due("2026-06-10T23:59:59Z", MemberType.Premium)) == "2026-07-01");
    }

    // CP-CORE-FINE-001: same-day 0, next-day 100, +3d 300, early 0.
    private static void FineCheck(Harness h)
    {
        // Loan 2026-06-10 => due 2026-06-24.
        var due = Due("2026-06-10T00:00:00Z"); // 2026-06-24
        h.Check("CP-CORE-FINE-001/due-day 23:59:59Z return -> 0 (FMEA-001)",
            LendingDomain.Fine(Inst("2026-06-24T23:59:59Z"), due) == 0);
        h.Check("CP-CORE-FINE-001/due+1 00:00:00Z return -> 100",
            LendingDomain.Fine(Inst("2026-06-25T00:00:00Z"), due) == 100);
        h.Check("CP-CORE-FINE-001/due+3 -> 300",
            LendingDomain.Fine(Inst("2026-06-27T12:00:00Z"), due) == 300);
        h.Check("CP-CORE-FINE-001/early return -> 0",
            LendingDomain.Fine(Inst("2026-06-20T00:00:00Z"), due) == 0);
    }

    // CP-CORE-OVERDUE-001: due-day not overdue (> only); due+1 blocks; returned excluded; any.
    private static void OverdueCheck(Harness h)
    {
        // active loan with due date 2026-06-24.
        var due = new DateOnly(2026, 6, 24);

        // new loan ON the due day => not overdue (strict >).
        var onDueDay = new LoanContext(true, true, Inst("2026-06-24T12:00:00Z"),
            new[] { due }, BookAvailableCopies: 10);
        h.Check("CP-CORE-OVERDUE-001/new loan on due day not blocked (> only)",
            LendingDecisions.EvaluateLoan(onDueDay) == LoanDecision.Allowed);

        // new loan due+1 => blocked.
        var dayAfter = onDueDay with { LoanedAtUtc = Inst("2026-06-25T00:00:00Z") };
        h.Check("CP-CORE-OVERDUE-001/new loan due+1 member_overdue_blocked",
            LendingDecisions.EvaluateLoan(dayAfter) == LoanDecision.MemberOverdueBlocked);

        // returned loan excluded: active due-date list empty => not blocked even far past due.
        var afterReturn = dayAfter with { MemberActiveLoanDueDates = Array.Empty<DateOnly>() };
        h.Check("CP-CORE-OVERDUE-001/returned overdue loan does not block",
            LendingDecisions.EvaluateLoan(afterReturn) == LoanDecision.Allowed);

        // any: 2 active, only 1 overdue => blocked.
        var anyOverdue = new LoanContext(true, true, Inst("2026-06-25T00:00:00Z"),
            new[] { new DateOnly(2099, 1, 1), due }, BookAvailableCopies: 10);
        h.Check("CP-CORE-OVERDUE-001/any: one of two active overdue blocks",
            LendingDecisions.EvaluateLoan(anyOverdue) == LoanDecision.MemberOverdueBlocked);
    }
}
