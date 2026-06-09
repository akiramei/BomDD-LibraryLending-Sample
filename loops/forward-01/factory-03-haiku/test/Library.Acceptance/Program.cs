using Library.Core;

var service = new LendingService();
var passed = 0;
var failed = 0;

// CP-CORE-AVAIL-001: Inventory management (INV-1)
{
    Console.WriteLine("=== CP-CORE-AVAIL-001: Inventory availability ===");

    // Test 1: copies=1, 1st loan succeeds, 2nd fails
    {
        var result1 = service.ValidateLoan(
            bookId: "bk_0000000001",
            memberId: "mb_0000000001",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 1,
            activeLoanCountForBook: 0,
            activeLoanCountForMember: 0,
            memberLoans: new List<(DateOnly, bool)>()
        );

        if (result1.IsValid)
        {
            Console.WriteLine("✓ 1st loan with copies=1 succeeds");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ 1st loan with copies=1 should succeed");
            failed++;
        }

        var result2 = service.ValidateLoan(
            bookId: "bk_0000000001",
            memberId: "mb_0000000002",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 1,
            activeLoanCountForBook: 1,
            activeLoanCountForMember: 0,
            memberLoans: new List<(DateOnly, bool)>()
        );

        if (!result2.IsValid && result2.ErrorCode == "no_copies_available")
        {
            Console.WriteLine("✓ 2nd loan with copies=1 fails with no_copies_available");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ 2nd loan with copies=1 should fail with no_copies_available");
            failed++;
        }
    }

    // Test 2: copies=2
    {
        var result1 = service.ValidateLoan(
            bookId: "bk_0000000002",
            memberId: "mb_0000000003",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 2,
            activeLoanCountForBook: 0,
            activeLoanCountForMember: 0,
            memberLoans: new List<(DateOnly, bool)>()
        );

        var result2 = service.ValidateLoan(
            bookId: "bk_0000000002",
            memberId: "mb_0000000004",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 2,
            activeLoanCountForBook: 1,
            activeLoanCountForMember: 0,
            memberLoans: new List<(DateOnly, bool)>()
        );

        var result3 = service.ValidateLoan(
            bookId: "bk_0000000002",
            memberId: "mb_0000000005",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 2,
            activeLoanCountForBook: 2,
            activeLoanCountForMember: 0,
            memberLoans: new List<(DateOnly, bool)>()
        );

        if (result1.IsValid && result2.IsValid && !result3.IsValid && result3.ErrorCode == "no_copies_available")
        {
            Console.WriteLine("✓ copies=2 allows 2 loans, 3rd fails");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ copies=2 should allow 2 loans, fail on 3rd");
            failed++;
        }
    }
}

// CP-CORE-LIMIT-001: Member loan limit (INV-2)
{
    Console.WriteLine("\n=== CP-CORE-LIMIT-001: Member active loan limit ===");

    // 3rd loan succeeds, 4th fails
    {
        var result3 = service.ValidateLoan(
            bookId: "bk_0000000003",
            memberId: "mb_0000000010",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 10,
            activeLoanCountForBook: 0,
            activeLoanCountForMember: 2,
            memberLoans: new List<(DateOnly, bool)>()
        );

        if (result3.IsValid)
        {
            Console.WriteLine("✓ 3rd loan for member succeeds");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ 3rd loan for member should succeed");
            failed++;
        }

        var result4 = service.ValidateLoan(
            bookId: "bk_0000000004",
            memberId: "mb_0000000010",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 10,
            activeLoanCountForBook: 0,
            activeLoanCountForMember: 3,
            memberLoans: new List<(DateOnly, bool)>()
        );

        if (!result4.IsValid && result4.ErrorCode == "loan_limit_exceeded")
        {
            Console.WriteLine("✓ 4th loan for member fails with loan_limit_exceeded");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ 4th loan for member should fail with loan_limit_exceeded");
            failed++;
        }
    }

    // After return, new loan succeeds
    {
        var result = service.ValidateLoan(
            bookId: "bk_0000000005",
            memberId: "mb_0000000010",
            loanedAtUtc: new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc),
            bookCopies: 10,
            activeLoanCountForBook: 0,
            activeLoanCountForMember: 2,
            memberLoans: new List<(DateOnly, bool)>()
        );

        if (result.IsValid)
        {
            Console.WriteLine("✓ After return, member can loan again");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ After return, member should be able to loan");
            failed++;
        }
    }
}

// CP-CORE-DUE-001: Due date calculation
{
    Console.WriteLine("\n=== CP-CORE-DUE-001: Due date calculation ===");

    // Test 1: Month boundary (Jan 31 + 14 = Feb 14)
    {
        var dueDate = service.CalculateDueDate(new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc));
        if (dueDate == new DateOnly(2026, 2, 14))
        {
            Console.WriteLine("✓ 2026-01-31 + 14 days = 2026-02-14");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ 2026-01-31 + 14 days should be 2026-02-14, got {dueDate}");
            failed++;
        }
    }

    // Test 2: Year boundary (Dec 25 + 14 = Jan 8 next year)
    {
        var dueDate = service.CalculateDueDate(new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc));
        if (dueDate == new DateOnly(2027, 1, 8))
        {
            Console.WriteLine("✓ 2026-12-25 + 14 days = 2027-01-08");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ 2026-12-25 + 14 days should be 2027-01-08, got {dueDate}");
            failed++;
        }
    }

    // Test 3: Time doesn't matter (23:59:59)
    {
        var dueDate = service.CalculateDueDate(new DateTime(2026, 6, 10, 23, 59, 59, DateTimeKind.Utc));
        if (dueDate == new DateOnly(2026, 6, 24))
        {
            Console.WriteLine("✓ 2026-06-10T23:59:59 + 14 days = 2026-06-24 (time ignored)");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ 2026-06-10T23:59:59 + 14 days should be 2026-06-24, got {dueDate}");
            failed++;
        }
    }
}

// CP-CORE-FINE-001: Fine calculation
{
    Console.WriteLine("\n=== CP-CORE-FINE-001: Fine calculation ===");

    var dueDate = new DateOnly(2026, 6, 24);

    // Same day return = 0
    {
        var fine = service.CalculateFine(
            new DateTime(2026, 6, 24, 23, 59, 59, DateTimeKind.Utc),
            dueDate
        );
        if (fine == 0)
        {
            Console.WriteLine("✓ Return on due date (23:59:59) = 0 fine");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ Return on due date should be 0, got {fine}");
            failed++;
        }
    }

    // Next day = 100
    {
        var fine = service.CalculateFine(
            new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc),
            dueDate
        );
        if (fine == 100)
        {
            Console.WriteLine("✓ Return on due date+1 (00:00:00) = 100 fine");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ Return on due date+1 should be 100, got {fine}");
            failed++;
        }
    }

    // +3 days = 300
    {
        var fine = service.CalculateFine(
            new DateTime(2026, 6, 27, 12, 30, 45, DateTimeKind.Utc),
            dueDate
        );
        if (fine == 300)
        {
            Console.WriteLine("✓ Return on due date+3 = 300 fine");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ Return on due date+3 should be 300, got {fine}");
            failed++;
        }
    }

    // Early return = 0
    {
        var fine = service.CalculateFine(
            new DateTime(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc),
            dueDate
        );
        if (fine == 0)
        {
            Console.WriteLine("✓ Early return (before due date) = 0 fine");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ Early return should be 0, got {fine}");
            failed++;
        }
    }
}

// CP-CORE-OVERDUE-001: Overdue blocking
{
    Console.WriteLine("\n=== CP-CORE-OVERDUE-001: Overdue blocking ===");

    // Due date is 2026-06-24
    var dueDate = new DateOnly(2026, 6, 24);

    // On due date, not overdue
    {
        var baseDate = new DateOnly(2026, 6, 24);
        var isOverdue = service.IsOverdue(baseDate, dueDate);
        if (!isOverdue)
        {
            Console.WriteLine("✓ On due date: not overdue");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ On due date should not be overdue");
            failed++;
        }
    }

    // Due date+1, is overdue
    {
        var baseDate = new DateOnly(2026, 6, 25);
        var isOverdue = service.IsOverdue(baseDate, dueDate);
        if (isOverdue)
        {
            Console.WriteLine("✓ On due date+1: is overdue");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ On due date+1 should be overdue");
            failed++;
        }
    }

    // Test with memberLoans (any pattern)
    // memberLoans contains the due dates, on 2026-06-24, dues of 2026-06-25 and later are not overdue
    {
        var loanDate = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
        var memberLoans = new List<(DateOnly, bool)>
        {
            (new DateOnly(2026, 6, 25), true),  // active, due in future, not overdue
            (new DateOnly(2026, 6, 26), true)   // active, due in future, not overdue
        };

        var result = service.ValidateLoan(
            bookId: "bk_0000000020",
            memberId: "mb_0000000020",
            loanedAtUtc: loanDate,
            bookCopies: 10,
            activeLoanCountForBook: 0,
            activeLoanCountForMember: 2,
            memberLoans: memberLoans
        );

        if (result.IsValid)
        {
            Console.WriteLine("✓ With non-overdue member loans, new loan succeeds");
            passed++;
        }
        else
        {
            Console.WriteLine($"✗ With non-overdue member loans, should succeed (got {result.ErrorCode})");
            failed++;
        }
    }

    // Test with one overdue loan (any pattern)
    {
        var loanDate = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
        var memberLoans = new List<(DateOnly, bool)>
        {
            (new DateOnly(2026, 6, 20), true),  // active, overdue (6-25 > 6-20)
            (new DateOnly(2026, 6, 23), true)   // active, overdue (6-25 > 6-23)
        };

        var result = service.ValidateLoan(
            bookId: "bk_0000000021",
            memberId: "mb_0000000021",
            loanedAtUtc: loanDate,
            bookCopies: 10,
            activeLoanCountForBook: 0,
            activeLoanCountForMember: 2,
            memberLoans: memberLoans
        );

        if (!result.IsValid && result.ErrorCode == "member_overdue_blocked")
        {
            Console.WriteLine("✓ With overdue member loans, new loan blocked");
            passed++;
        }
        else
        {
            Console.WriteLine("✗ With overdue member loans, should be blocked");
            failed++;
        }
    }
}

// Summary
Console.WriteLine($"\n=== SUMMARY ===");
Console.WriteLine($"Passed: {passed}");
Console.WriteLine($"Failed: {failed}");

if (failed > 0)
{
    Environment.Exit(1);
}
else
{
    Environment.Exit(0);
}
