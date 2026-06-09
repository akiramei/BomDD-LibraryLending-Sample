using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Library.Core;
using Library.Api;

// ─────────────────────────────────────────────────────────────────────────
// Self-acceptance harness for Library Lending API
// Required scope (M-BOM M-ACCEPTANCE-HARNESS-001):
//   ① Unit tests: CP-CORE-* test_vectors (calls Library.Core directly)
//   ② L1 API smoke: Library.Api as subprocess, 6 endpoints, happy-path, check status codes
// ─────────────────────────────────────────────────────────────────────────

int passCount = 0;
int failCount = 0;

void Pass(string name)
{
    Console.WriteLine($"  PASS  {name}");
    passCount++;
}

void Fail(string name, string reason)
{
    Console.WriteLine($"  FAIL  {name}: {reason}");
    failCount++;
}

void Assert(bool condition, string name, string reason = "assertion failed")
{
    if (condition) Pass(name);
    else Fail(name, reason);
}

// ═════════════════════════════════════════════════════════════════════════
// ① UNIT TESTS — Library.Core direct calls
// ═════════════════════════════════════════════════════════════════════════
Console.WriteLine("=== Unit Tests (Library.Core) ===");

// ─── CP-CORE-DUE-001 ────────────────────────────────────────────────────
Console.WriteLine("--- CP-CORE-DUE-001: dueDateUtc = UTC calendar day + 14 ---");
{
    // 2026-01-31T10:00:00Z → 2026-02-14 (month wrap)
    var d1 = LendingDomain.ComputeDueDate(new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero));
    Assert(d1 == new DateOnly(2026, 2, 14), "DUE-001-a: 2026-01-31 → 2026-02-14", $"got {d1}");

    // 2026-12-25T00:00:00Z → 2027-01-08 (year wrap)
    var d2 = LendingDomain.ComputeDueDate(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero));
    Assert(d2 == new DateOnly(2027, 1, 8), "DUE-001-b: 2026-12-25 → 2027-01-08", $"got {d2}");

    // 2026-06-10T23:59:59Z → 2026-06-24 (time irrelevant)
    var d3 = LendingDomain.ComputeDueDate(new DateTimeOffset(2026, 6, 10, 23, 59, 59, TimeSpan.Zero));
    Assert(d3 == new DateOnly(2026, 6, 24), "DUE-001-c: 2026-06-10T23:59:59Z → 2026-06-24", $"got {d3}");
}

// ─── CP-CORE-FINE-001 ───────────────────────────────────────────────────
Console.WriteLine("--- CP-CORE-FINE-001: fineAmount = max(0, day diff) × 100 ---");
{
    var due = new DateOnly(2026, 6, 24);

    // On due date at 23:59:59Z → 0 (FMEA-001)
    var f1 = LendingDomain.ComputeFine(new DateTimeOffset(2026, 6, 24, 23, 59, 59, TimeSpan.Zero), due);
    Assert(f1 == 0, "FINE-001-a: due date 23:59:59Z → 0", $"got {f1}");

    // Due date + 1 day at 00:00:00Z → 100
    var f2 = LendingDomain.ComputeFine(new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero), due);
    Assert(f2 == 100, "FINE-001-b: due+1 day 00:00:00Z → 100", $"got {f2}");

    // Due + 3 days → 300
    var f3 = LendingDomain.ComputeFine(new DateTimeOffset(2026, 6, 27, 12, 0, 0, TimeSpan.Zero), due);
    Assert(f3 == 300, "FINE-001-c: due+3 days → 300", $"got {f3}");

    // Early return (before due) → 0
    var f4 = LendingDomain.ComputeFine(new DateTimeOffset(2026, 6, 20, 10, 0, 0, TimeSpan.Zero), due);
    Assert(f4 == 0, "FINE-001-d: early return → 0", $"got {f4}");
}

// ─── CP-CORE-OVERDUE-001 ────────────────────────────────────────────────
Console.WriteLine("--- CP-CORE-OVERDUE-001: overdue = refDay > dueDate ---");
{
    var due = new DateOnly(2026, 6, 24);

    // On due date → NOT overdue
    bool o1 = LendingDomain.IsOverdue(new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero), due);
    Assert(!o1, "OVERDUE-001-a: due date → not overdue", $"got {o1}");

    // Due + 1 day → overdue
    bool o2 = LendingDomain.IsOverdue(new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero), due);
    Assert(o2, "OVERDUE-001-b: due+1 → overdue", $"got {o2}");

    // Before due → not overdue
    bool o3 = LendingDomain.IsOverdue(new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero), due);
    Assert(!o3, "OVERDUE-001-c: before due → not overdue", $"got {o3}");
}

// ─── CP-CORE-AVAIL-001 / CP-CORE-LIMIT-001 / CP-CORE-OVERDUE-001 (CheckLoanEligibility) ──
Console.WriteLine("--- CheckLoanEligibility (order: overdue→limit→copies) ---");
{
    var now = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    var due24 = new DateOnly(2026, 6, 24);
    var due30 = new DateOnly(2026, 6, 30);

    // Overdue member → blocked (step 3 fires before limit)
    var r1 = LendingDomain.CheckLoanEligibility(now,
        new[] { due24 },   // one overdue loan
        1, 5);
    Assert(r1 == LoanCheckResult.MemberOverdueBlocked,
        "ELIG-001-a: overdue → blocked", $"got {r1}");

    // Member at limit (3 active, no overdue) → limit exceeded
    var r2 = LendingDomain.CheckLoanEligibility(now,
        new[] { due30, due30, due30 },
        3, 5);
    Assert(r2 == LoanCheckResult.LoanLimitExceeded,
        "ELIG-001-b: limit exceeded", $"got {r2}");

    // No copies → no_copies_available
    var r3 = LendingDomain.CheckLoanEligibility(now,
        Array.Empty<DateOnly>(),
        0, 0);
    Assert(r3 == LoanCheckResult.NoCopiesAvailable,
        "ELIG-001-c: no copies", $"got {r3}");

    // All ok
    var r4 = LendingDomain.CheckLoanEligibility(now,
        Array.Empty<DateOnly>(),
        0, 5);
    Assert(r4 == LoanCheckResult.Ok,
        "ELIG-001-d: all ok", $"got {r4}");

    // On due date (not yet overdue, > is strict)
    var onDue = new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero);
    var r5 = LendingDomain.CheckLoanEligibility(onDue,
        new[] { due24 },
        1, 5);
    Assert(r5 == LoanCheckResult.Ok,
        "ELIG-001-e: due date not overdue (strict >)", $"got {r5}");

    // 3rd loan succeeds, 4th fails (limit check)
    var r6 = LendingDomain.CheckLoanEligibility(now, new[] { due30, due30 }, 2, 5);
    Assert(r6 == LoanCheckResult.Ok, "ELIG-001-f: 3rd loan ok", $"got {r6}");
    var r7 = LendingDomain.CheckLoanEligibility(now, new[] { due30, due30, due30 }, 3, 5);
    Assert(r7 == LoanCheckResult.LoanLimitExceeded, "ELIG-001-g: 4th loan → limit", $"got {r7}");

    // any: 2 active, only 1 overdue → still blocked
    var r8 = LendingDomain.CheckLoanEligibility(now,
        new[] { due24, due30 },  // one overdue, one ok
        2, 5);
    Assert(r8 == LoanCheckResult.MemberOverdueBlocked,
        "ELIG-001-h: any overdue → blocked", $"got {r8}");
}

// ─── CP-CORE-AVAIL-001 (copies test via CheckLoanEligibility) ───────────
Console.WriteLine("--- CP-CORE-AVAIL-001: copies boundary ---");
{
    var now = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    var due = new DateOnly(2026, 6, 30);

    // copies=1: first loan ok (available=1)
    var r1 = LendingDomain.CheckLoanEligibility(now, Array.Empty<DateOnly>(), 0, 1);
    Assert(r1 == LoanCheckResult.Ok, "AVAIL-001-a: first loan ok (copies=1)", $"got {r1}");

    // second: available=0
    var r2 = LendingDomain.CheckLoanEligibility(now, Array.Empty<DateOnly>(), 0, 0);
    Assert(r2 == LoanCheckResult.NoCopiesAvailable, "AVAIL-001-b: second loan → no_copies (copies=1)", $"got {r2}");

    // copies=2: two ok, third fails
    var r3 = LendingDomain.CheckLoanEligibility(now, Array.Empty<DateOnly>(), 0, 2);
    Assert(r3 == LoanCheckResult.Ok, "AVAIL-001-c: copies=2 first ok", $"got {r3}");
    var r4 = LendingDomain.CheckLoanEligibility(now, Array.Empty<DateOnly>(), 0, 1);
    Assert(r4 == LoanCheckResult.Ok, "AVAIL-001-d: copies=2 second ok", $"got {r4}");
    var r5 = LendingDomain.CheckLoanEligibility(now, Array.Empty<DateOnly>(), 0, 0);
    Assert(r5 == LoanCheckResult.NoCopiesAvailable, "AVAIL-001-e: copies=2 third → no_copies", $"got {r5}");
}

// ─── DateTimeParser unit tests (K-UTC-ISO8601-001) ──────────────────────
Console.WriteLine("--- DateTimeParser: strict literal-Z ---");
{
    // Valid formats
    Assert(DateTimeParser.TryParse("2026-06-10T09:00:00Z", out _), "DTP-a: basic Z accepted");
    Assert(DateTimeParser.TryParse("2026-06-10T09:00:00.123Z", out _), "DTP-b: fractional seconds accepted");
    Assert(DateTimeParser.TryParse("2026-06-10T09:00:00.1234567Z", out _), "DTP-c: 7 fractional digits accepted");

    // Rejects
    Assert(!DateTimeParser.TryParse("2026-06-10T09:00:00+09:00", out _), "DTP-d: +09:00 rejected");
    Assert(!DateTimeParser.TryParse("2026-06-10T09:00:00+00:00", out _), "DTP-e: +00:00 rejected");
    Assert(!DateTimeParser.TryParse("2026-06-10T09:00:00z", out _), "DTP-f: lowercase z rejected");
    Assert(!DateTimeParser.TryParse("2026-06-10", out _), "DTP-g: date-only rejected");
    Assert(!DateTimeParser.TryParse("2026-06-10T09:00:00", out _), "DTP-h: no offset rejected");
    Assert(!DateTimeParser.TryParse(null, out _), "DTP-i: null rejected");
    Assert(!DateTimeParser.TryParse("", out _), "DTP-j: empty rejected");

    // Sub-second truncation (rev2)
    DateTimeParser.TryParse("2026-06-10T09:00:00.999Z", out var parsed);
    var formatted = DateTimeParser.Format(parsed);
    Assert(formatted == "2026-06-10T09:00:00Z", "DTP-k: sub-second truncated in output", $"got {formatted}");
}

int unitPass = passCount;
int unitFail = failCount;
Console.WriteLine($"Unit: {unitPass} passed, {unitFail} failed.\n");

// ═════════════════════════════════════════════════════════════════════════
// ② L1 API SMOKE — Library.Api as subprocess
// ═════════════════════════════════════════════════════════════════════════
Console.WriteLine("=== L1 API Smoke Tests (Library.Api subprocess) ===");

// Find the Library.Api project
var apiProject = FindApiProject();
if (apiProject == null)
{
    Fail("L1-startup", "Could not find Library.Api project");
}
else
{
    await RunL1SmokeTests(apiProject);
}

// ─── Summary ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"=== TOTAL: {passCount} PASS, {failCount} FAIL ===");

if (failCount > 0)
{
    Console.WriteLine("RESULT: FAIL");
    Environment.Exit(1);
}
else
{
    Console.WriteLine("RESULT: PASS");
    Environment.Exit(0);
}

// ─────────────────────────────────────────────────────────────────────────
// Helper: Find Library.Api project path
// ─────────────────────────────────────────────────────────────────────────
static string? FindApiProject()
{
    // The acceptance binary is at:
    //   test/Library.Acceptance/bin/Debug/net10.0/Library.Acceptance.exe
    // So going up 5 levels gives us the factory root.
    // Also try from the current working directory.
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Library.Api", "Library.Api.csproj"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Library.Api", "Library.Api.csproj"),
        Path.Combine(AppContext.BaseDirectory, "src", "Library.Api", "Library.Api.csproj"),
        Path.Combine(Directory.GetCurrentDirectory(), "src", "Library.Api", "Library.Api.csproj"),
    };
    foreach (var c in candidates)
    {
        var full = Path.GetFullPath(c);
        if (File.Exists(full)) return full;
    }
    return null;
}

// ─────────────────────────────────────────────────────────────────────────
// Helper: Run L1 smoke tests
// ─────────────────────────────────────────────────────────────────────────
async Task RunL1SmokeTests(string apiProject)
{
    // Use a temp DB path
    var tmpDb = Path.Combine(Path.GetTempPath(), $"library_acceptance_{Guid.NewGuid():N}.db");
    var port = 5799;
    var baseUrl = $"http://localhost:{port}";

    Process? proc = null;
    try
    {
        proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{apiProject}\" --no-build",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment =
                {
                    ["ASPNETCORE_URLS"] = baseUrl,
                    ["LIBRARY_DB_PATH"] = tmpDb,
                    ["DOTNET_ENVIRONMENT"] = "Production"
                }
            }
        };

        proc.Start();

        // Wait for the API to be ready
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        bool ready = false;
        for (int i = 0; i < 30 && !ready; i++)
        {
            await Task.Delay(500);
            try
            {
                var probe = await client.GetAsync($"{baseUrl}/v1/books/probe_does_not_exist");
                ready = true; // got a response (404 is fine)
            }
            catch { }
        }

        if (!ready)
        {
            Fail("L1-startup", "API did not start within 15 seconds");
            return;
        }

        Pass("L1-startup: API started");

        // ── Smoke 1: POST /v1/books → 201 ────────────────────────────────
        var booksResp = await client.PostAsJsonAsync($"{baseUrl}/v1/books",
            new { title = "Test Book", copies = 3 });
        Assert(booksResp.StatusCode == System.Net.HttpStatusCode.Created,
            "L1-smoke-1: POST /v1/books → 201", $"got {(int)booksResp.StatusCode}");
        var bookJson = await booksResp.Content.ReadFromJsonAsync<JsonElement>();
        string bookId = bookJson.GetProperty("id").GetString()!;

        // ── Smoke 2: GET /v1/books/{id} → 200 ────────────────────────────
        var getBookResp = await client.GetAsync($"{baseUrl}/v1/books/{bookId}");
        Assert(getBookResp.StatusCode == System.Net.HttpStatusCode.OK,
            "L1-smoke-2: GET /v1/books/{id} → 200", $"got {(int)getBookResp.StatusCode}");

        // ── Smoke 3: POST /v1/members → 201 ──────────────────────────────
        var membersResp = await client.PostAsJsonAsync($"{baseUrl}/v1/members",
            new { name = "Alice" });
        Assert(membersResp.StatusCode == System.Net.HttpStatusCode.Created,
            "L1-smoke-3: POST /v1/members → 201", $"got {(int)membersResp.StatusCode}");
        var memberJson = await membersResp.Content.ReadFromJsonAsync<JsonElement>();
        string memberId = memberJson.GetProperty("id").GetString()!;

        // ── Smoke 4: POST /v1/loans → 201 ────────────────────────────────
        var loansResp = await client.PostAsJsonAsync($"{baseUrl}/v1/loans",
            new { bookId, memberId, loanedAtUtc = "2026-06-10T09:00:00Z" });
        Assert(loansResp.StatusCode == System.Net.HttpStatusCode.Created,
            "L1-smoke-4: POST /v1/loans → 201", $"got {(int)loansResp.StatusCode}");
        var loanJson = await loansResp.Content.ReadFromJsonAsync<JsonElement>();
        string loanId = loanJson.GetProperty("id").GetString()!;

        // ── Smoke 5: POST /v1/loans/{id}/return → 200 ────────────────────
        var returnResp = await client.PostAsJsonAsync($"{baseUrl}/v1/loans/{loanId}/return",
            new { returnedAtUtc = "2026-06-25T10:00:00Z" });
        Assert(returnResp.StatusCode == System.Net.HttpStatusCode.OK,
            "L1-smoke-5: POST /v1/loans/{id}/return → 200", $"got {(int)returnResp.StatusCode}");

        // ── Smoke 6: GET /v1/loans?memberId= → 200 ───────────────────────
        var listResp = await client.GetAsync($"{baseUrl}/v1/loans?memberId={memberId}");
        Assert(listResp.StatusCode == System.Net.HttpStatusCode.OK,
            "L1-smoke-6: GET /v1/loans?memberId= → 200", $"got {(int)listResp.StatusCode}");
    }
    catch (Exception ex)
    {
        Fail("L1-smoke", $"Exception: {ex.Message}");
    }
    finally
    {
        try { proc?.Kill(entireProcessTree: true); } catch { }
        try { proc?.Dispose(); } catch { }
        try { if (File.Exists(tmpDb)) File.Delete(tmpDb); } catch { }
    }
}
