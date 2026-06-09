# Cheat Report - factory-06-haiku-retry

## Final Acceptance Status
- **Build**: PASS (`dotnet build Library.sln` successful)
- **Unit Tests**: 13 PASS, 3 FAIL (out of 16 CP-CORE-* tests)
- **L1 API Smoke**: 6 PASS (all endpoints: POST /v1/books, GET /v1/books/{id}, POST /v1/members, POST /v1/loans, POST /v1/loans/{id}/return, GET /v1/loans)
- **Self-Acceptance Exit Code**: 1 (due to unit test failures)

## Blocker Cheats (3 failing tests)

### CHEAT-F06-001 [arithmetic] Due date calculation off-by-one
- 手法が与えなかったもの: DateOnly.AddDays() arithmetic in context of date-only calculations.
- 代替した判断: Implemented as `new DateTime(year, month, day, 0, 0, 0).AddDays(14)` but results show 2026-06-10 + 14 days = 2026-06-25 instead of expected 2026-06-24. Two other date calculations (Jan 31, Dec 25) pass correctly, indicating an edge case with June dates or specific year boundaries.
- 重大度: blocker (fails CP-CORE-DUE-001: Time doesn't matter; affects fine calculation which depends on correct due date)

### CHEAT-F06-002 [boundary] Fine calculation at midnight boundary  
- 手法が与えなかったもの: Exact interpretation of "same calendar day return = 0 fine" when return timestamp is 23:59:59Z on the due date.
- 代替した判断: Implemented as `if (returnedDate > dueDate) then days_beyond = returnedDate - dueDate else 0`. Test expects fine=0 for return on due date 2026-06-24 at 23:59:59Z, but calculation produces fine=100.
- 重大度: blocker (fails CP-CORE-FINE-001: Same day return = 0 fine)

### CHEAT-F06-003 [boundary] Overdue block on due date edge case
- 手法が与えなかったもの: Exact behavior of overdue check when attempting to loan ON the due date of an existing active loan.
- 代替した判断: Code implements `if (loanedDate > existingDueDate)` (strict greater-than) which should NOT block on the due date itself. Test appears to create an overdue situation where it should not. Root cause may be linked to CHEAT-F06-001 due date off-by-one.
- 重大度: blocker (fails CP-CORE-OVERDUE-001: Due date same day = not blocked)

## Non-Blocker Implementation Notes

### CHEAT-F06-004 [infra] ASP.NET Core launch settings and path resolution
- 手法が与えなかったもの: Launch settings.json profile behavior and working directory context for subprocess API execution.
- 代替した判断: Discovered that dotnet run uses launchSettings.json by default (port 5228), requiring --no-launch-profile flag. Test harness path resolution used AppContext.BaseDirectory and relative paths, which failed. Solution: absolute path via `Path.GetFullPath(AppContext.BaseDirectory + ".....")` and --no-launch-profile.
- 重大度: minor (resolved; all 6 L1 endpoints now working)

### CHEAT-F06-005 [framework] JSON serialization for conditional response fields
- 手法が与えなかったもの: .NET JSON serialization patterns for excluding fields from response objects based on state (e.g., active loans should not include returnedAtUtc/fineAmount).
- 代替した判断: Cannot use null-coalescing on anonymous object literals in .NET minimal APIs due to type inference issues. Workaround: SelectMany with separate object expressions for active vs returned loans, cast as IEnumerable<object>.
- 重大度: minor (working as designed; API returns correct schema per spec)

## Summary of Deviations from Specification

1. **Due date arithmetic** (1 test affected): Unexplained +1 day offset in June 2026 due date calculation. Jan 31→Feb 14 and Dec 25→Jan 8 calculations pass; June 10→June 24 fails with June 25 result.

2. **Fine boundary case** (1 test affected): Same-day return at 23:59:59Z incorrectly assessed as fine=100 instead of fine=0.

3. **Overdue block boundary** (1 test affected): Likely cascading from issue #1; unable to diagnose independently.

All other 13 unit tests pass. All 6 L1 API endpoints respond with correct HTTP status and schema. Manufacturing is complete per M-BOM scope, though **self-acceptance does not achieve full PASS due to 3 unit test failures**.

No database schema issues discovered; L1 smoke tests verify persistence works (GET after POST succeeds).
