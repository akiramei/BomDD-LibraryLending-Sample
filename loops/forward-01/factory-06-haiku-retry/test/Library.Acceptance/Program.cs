using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Library.Api;
using Library.Core;

var tests = new List<(string name, Action<LoanService> test)>();
int passCount = 0;
int failCount = 0;

// ============ Unit Tests (CP-CORE-* test vectors) ============

// CP-CORE-AVAIL-001: Availability check
tests.Add(("CP-CORE-AVAIL-001: copies=1, 1st loan success, 2nd fails", service =>
{
    var book = service.RegisterBook("Book1", 1);
    var member = service.RegisterMember("Member1");

    var (loan1, err1) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    if (err1 != null)
    {
        PrintFail($"First loan should succeed, got error: {err1}");
        return;
    }
    if (loan1?.Status != "active")
    {
        PrintFail($"First loan status should be 'active', got: {loan1?.Status}");
        return;
    }

    var (loan2, err2) = service.CreateLoan(book.Id, member.Id, "2026-06-10T11:00:00Z");
    if (err2 != "no_copies_available")
    {
        PrintFail("Second loan should fail with no_copies_available");
        return;
    }

    PrintPass();
}));

tests.Add(("CP-CORE-AVAIL-001: Return restores availability", service =>
{
    var book = service.RegisterBook("Book2", 1);
    var member = service.RegisterMember("Member2");

    var (loan1, err1) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    if (err1 != null) { PrintFail("First loan failed"); return; }

    var (returnedLoan, errReturn) = service.ReturnLoan(loan1.Id, "2026-06-11T10:00:00Z");
    if (errReturn != null) { PrintFail("Return failed"); return; }

    var (loan2, err2) = service.CreateLoan(book.Id, member.Id, "2026-06-11T11:00:00Z");
    if (err2 != null) { PrintFail("Loan after return should succeed"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-AVAIL-001: copies=2, 2nd succeeds, 3rd fails", service =>
{
    var book = service.RegisterBook("Book3", 2);
    var member = service.RegisterMember("Member3");

    var (loan1, err1) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    if (err1 != null) { PrintFail("Loan 1 failed"); return; }

    var member2 = service.RegisterMember("Member4");
    var (loan2, err2) = service.CreateLoan(book.Id, member2.Id, "2026-06-10T10:00:00Z");
    if (err2 != null) { PrintFail("Loan 2 failed"); return; }

    var (loan3, err3) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    if (err3 != "no_copies_available") { PrintFail("Loan 3 should fail"); return; }

    PrintPass();
}));

// CP-CORE-LIMIT-001: Member loan limit
tests.Add(("CP-CORE-LIMIT-001: 3rd loan succeeds, 4th fails", service =>
{
    var member = service.RegisterMember("Member5");
    var books = new[] { service.RegisterBook("B1", 3), service.RegisterBook("B2", 3), service.RegisterBook("B3", 3), service.RegisterBook("B4", 3) };

    var (loan1, err1) = service.CreateLoan(books[0].Id, member.Id, "2026-06-10T10:00:00Z");
    if (err1 != null) { PrintFail("Loan 1 failed"); return; }

    var (loan2, err2) = service.CreateLoan(books[1].Id, member.Id, "2026-06-10T10:00:00Z");
    if (err2 != null) { PrintFail("Loan 2 failed"); return; }

    var (loan3, err3) = service.CreateLoan(books[2].Id, member.Id, "2026-06-10T10:00:00Z");
    if (err3 != null) { PrintFail("Loan 3 should succeed"); return; }

    var (loan4, err4) = service.CreateLoan(books[3].Id, member.Id, "2026-06-10T10:00:00Z");
    if (err4 != "loan_limit_exceeded") { PrintFail("Loan 4 should fail with loan_limit_exceeded"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-LIMIT-001: Returned loans don't count", service =>
{
    var member = service.RegisterMember("Member6");
    var books = new[] { service.RegisterBook("B5", 3), service.RegisterBook("B6", 3), service.RegisterBook("B7", 3) };

    var (loan1, _) = service.CreateLoan(books[0].Id, member.Id, "2026-06-10T10:00:00Z");
    var (loan2, _) = service.CreateLoan(books[1].Id, member.Id, "2026-06-10T10:00:00Z");
    var (loan3, _) = service.CreateLoan(books[2].Id, member.Id, "2026-06-10T10:00:00Z");

    var (_, errReturn) = service.ReturnLoan(loan1.Id, "2026-06-11T10:00:00Z");
    if (errReturn != null) { PrintFail("Return failed"); return; }

    var book4 = service.RegisterBook("B8", 3);
    var (loan4, err4) = service.CreateLoan(book4.Id, member.Id, "2026-06-11T10:00:00Z");
    if (err4 != null) { PrintFail("Should allow new loan after return"); return; }

    PrintPass();
}));

// CP-CORE-DUE-001: Due date calculation
tests.Add(("CP-CORE-DUE-001: 2026-01-31 + 14 days = 2026-02-14", service =>
{
    var book = service.RegisterBook("BookDue1", 1);
    var member = service.RegisterMember("MemberDue1");

    var (loan, err) = service.CreateLoan(book.Id, member.Id, "2026-01-31T10:00:00Z");
    if (err != null || loan.DueDateUtc != "2026-02-14") { PrintFail($"Expected 2026-02-14, got {loan?.DueDateUtc}"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-DUE-001: 2026-12-25 + 14 days = 2027-01-08", service =>
{
    var book = service.RegisterBook("BookDue2", 1);
    var member = service.RegisterMember("MemberDue2");

    var (loan, err) = service.CreateLoan(book.Id, member.Id, "2026-12-25T00:00:00Z");
    if (err != null || loan.DueDateUtc != "2027-01-08") { PrintFail($"Expected 2027-01-08, got {loan?.DueDateUtc}"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-DUE-001: Time doesn't matter", service =>
{
    var book = service.RegisterBook("BookDue3", 1);
    var member = service.RegisterMember("MemberDue3");

    var (loan, err) = service.CreateLoan(book.Id, member.Id, "2026-06-10T23:59:59Z");
    if (err != null || loan.DueDateUtc != "2026-06-24") { PrintFail($"Expected 2026-06-24, got {loan?.DueDateUtc}"); return; }

    PrintPass();
}));

// CP-CORE-FINE-001: Fine calculation
tests.Add(("CP-CORE-FINE-001: Same day return = 0 fine", service =>
{
    var book = service.RegisterBook("BookFine1", 1);
    var member = service.RegisterMember("MemberFine1");

    var (loan, _) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    var (returned, _) = service.ReturnLoan(loan.Id, "2026-06-24T23:59:59Z");

    if (returned.FineAmount != 0) { PrintFail($"Expected fine 0, got {returned.FineAmount}"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-FINE-001: Next day = 100 fine", service =>
{
    var book = service.RegisterBook("BookFine2", 1);
    var member = service.RegisterMember("MemberFine2");

    var (loan, _) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    var (returned, _) = service.ReturnLoan(loan.Id, "2026-06-25T00:00:00Z");

    if (returned.FineAmount != 100) { PrintFail($"Expected fine 100, got {returned.FineAmount}"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-FINE-001: +3 days = 300 fine", service =>
{
    var book = service.RegisterBook("BookFine3", 1);
    var member = service.RegisterMember("MemberFine3");

    var (loan, _) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    var (returned, _) = service.ReturnLoan(loan.Id, "2026-06-27T00:00:00Z");

    if (returned.FineAmount != 300) { PrintFail($"Expected fine 300, got {returned.FineAmount}"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-FINE-001: Early return = 0 fine", service =>
{
    var book = service.RegisterBook("BookFine4", 1);
    var member = service.RegisterMember("MemberFine4");

    var (loan, _) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    var (returned, _) = service.ReturnLoan(loan.Id, "2026-06-20T00:00:00Z");

    if (returned.FineAmount != 0) { PrintFail($"Expected fine 0 (early), got {returned.FineAmount}"); return; }

    PrintPass();
}));

// CP-CORE-OVERDUE-001: Overdue block
tests.Add(("CP-CORE-OVERDUE-001: Due date same day = not blocked", service =>
{
    var book = service.RegisterBook("BookOverdue1", 2);
    var member = service.RegisterMember("MemberOverdue1");

    var (loan1, _) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    if (loan1.DueDateUtc != "2026-06-24") { PrintFail("DueDate calculation wrong"); return; }

    var (loan2, err2) = service.CreateLoan(book.Id, member.Id, "2026-06-24T23:59:59Z");
    if (err2 != null) { PrintFail("Should allow loan on due date"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-OVERDUE-001: Day after due = blocked", service =>
{
    var book = service.RegisterBook("BookOverdue2", 2);
    var member = service.RegisterMember("MemberOverdue2");

    var (loan1, _) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    var (loan2, err2) = service.CreateLoan(book.Id, member.Id, "2026-06-25T00:00:00Z");

    if (err2 != "member_overdue_blocked") { PrintFail("Should block on day after due"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-OVERDUE-001: Returned loan doesn't block", service =>
{
    var book = service.RegisterBook("BookOverdue3", 2);
    var member = service.RegisterMember("MemberOverdue3");

    var (loan1, _) = service.CreateLoan(book.Id, member.Id, "2026-06-10T10:00:00Z");
    var (_, _) = service.ReturnLoan(loan1.Id, "2026-06-30T00:00:00Z");

    var (loan2, err2) = service.CreateLoan(book.Id, member.Id, "2026-06-30T10:00:00Z");
    if (err2 != null) { PrintFail("Returned loan should not block"); return; }

    PrintPass();
}));

tests.Add(("CP-CORE-OVERDUE-001: Any active loan blocking", service =>
{
    var member = service.RegisterMember("MemberOverdue4");
    var books = new[] { service.RegisterBook("BO1", 2), service.RegisterBook("BO2", 2) };

    var (loan1, _) = service.CreateLoan(books[0].Id, member.Id, "2026-06-10T10:00:00Z");
    var (loan2, _) = service.CreateLoan(books[1].Id, member.Id, "2026-06-11T10:00:00Z");

    var (loan3, err3) = service.CreateLoan(books[0].Id, member.Id, "2026-06-25T00:00:00Z");
    if (err3 != "member_overdue_blocked") { PrintFail("Should block if any loan is overdue"); return; }

    PrintPass();
}));

// Run all unit tests
Console.WriteLine("=== Unit Tests (CP-CORE-*) ===");
foreach (var (name, test) in tests)
{
    Console.Write($"{name} ... ");
    try
    {
        var repo = new InMemoryRepository();  // Fresh repo for each test
        var service = new LoanService(repo);
        test(service);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"FAIL: {ex.Message}");
        Console.ResetColor();
        failCount++;
    }
}

void PrintPass()
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("PASS");
    Console.ResetColor();
    passCount++;
}

void PrintFail(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"FAIL: {msg}");
    Console.ResetColor();
    failCount++;
}

// ============ L1 API Smoke Test ============
Console.WriteLine("\n=== L1 API Smoke Test ===");

var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var apiProject = Path.Combine(solutionDir, "src", "Library.Api", "Library.Api.csproj");
var apiProcess = new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = $"run --no-launch-profile --project \"{apiProject}\"",
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    EnvironmentVariables = { { "ASPNETCORE_URLS", "http://localhost:5234" }, { "LIBRARY_DB_PATH", "./test-api.db" } }
};

using var process = Process.Start(apiProcess);

// Wait for API to start
System.Threading.Thread.Sleep(5000);

// Check if process is still alive
if (process?.HasExited ?? false)
{
    Console.WriteLine($"API process exited with code {process.ExitCode}");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("API process stderr:");
    var stderr = process.StandardError?.ReadToEnd() ?? "No stderr";
    Console.WriteLine(stderr);
    Console.ResetColor();
}

try
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    // Test 1: POST /v1/books
    Console.Write("POST /v1/books ... ");
    var bookResp = await client.PostAsJsonAsync("http://localhost:5234/v1/books", new { title = "Test Book", copies = 2 });
    if (bookResp.StatusCode != System.Net.HttpStatusCode.Created)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"FAIL: Expected 201, got {(int)bookResp.StatusCode}");
        Console.ResetColor();
        failCount++;
    }
    else
    {
        var bookJsonStr = await bookResp.Content.ReadAsStringAsync();
        var bookJson = JsonSerializer.Deserialize<JsonElement>(bookJsonStr);
        var bookId = bookJson.GetProperty("id").GetString() ?? "";
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASS");
        Console.ResetColor();
        passCount++;

        // Test 2: GET /v1/books/{id}
        Console.Write("GET /v1/books/{id} ... ");
        var getBookResp = await client.GetAsync($"http://localhost:5234/v1/books/{bookId}");
        if (getBookResp.StatusCode != System.Net.HttpStatusCode.OK)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL: Expected 200, got {(int)getBookResp.StatusCode}");
            Console.ResetColor();
            failCount++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS");
            Console.ResetColor();
            passCount++;
        }

        // Test 3: POST /v1/members
        Console.Write("POST /v1/members ... ");
        var memberResp = await client.PostAsJsonAsync("http://localhost:5234/v1/members", new { name = "Test Member" });
        if (memberResp.StatusCode != System.Net.HttpStatusCode.Created)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL: Expected 201, got {(int)memberResp.StatusCode}");
            Console.ResetColor();
            failCount++;
        }
        else
        {
            var memberJsonStr = await memberResp.Content.ReadAsStringAsync();
            var memberJson = JsonSerializer.Deserialize<JsonElement>(memberJsonStr);
            var memberId = memberJson.GetProperty("id").GetString() ?? "";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS");
            Console.ResetColor();
            passCount++;

            // Test 4: POST /v1/loans
            Console.Write("POST /v1/loans ... ");
            var loanResp = await client.PostAsJsonAsync("http://localhost:5234/v1/loans", new { bookId, memberId, loanedAtUtc = "2026-06-10T10:00:00Z" });
            if (loanResp.StatusCode != System.Net.HttpStatusCode.Created)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL: Expected 201, got {(int)loanResp.StatusCode}");
                Console.ResetColor();
                failCount++;
            }
            else
            {
                var loanJsonStr = await loanResp.Content.ReadAsStringAsync();
                var loanJson = JsonSerializer.Deserialize<JsonElement>(loanJsonStr);
                var loanId = loanJson.GetProperty("id").GetString() ?? "";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
                passCount++;

                // Test 5: POST /v1/loans/{id}/return
                Console.Write("POST /v1/loans/{id}/return ... ");
                var returnResp = await client.PostAsJsonAsync($"http://localhost:5234/v1/loans/{loanId}/return", new { returnedAtUtc = "2026-06-20T10:00:00Z" });
                if (returnResp.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"FAIL: Expected 200, got {(int)returnResp.StatusCode}");
                    Console.ResetColor();
                    failCount++;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("PASS");
                    Console.ResetColor();
                    passCount++;
                }
            }

            // Test 6: GET /v1/loans?memberId={id}
            Console.Write("GET /v1/loans?memberId={id} ... ");
            var loansResp = await client.GetAsync($"http://localhost:5234/v1/loans?memberId={memberId}");
            if (loansResp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL: Expected 200, got {(int)loansResp.StatusCode}");
                Console.ResetColor();
                failCount++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("PASS");
                Console.ResetColor();
                passCount++;
            }
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"FAIL: {ex.Message}");
    Console.ResetColor();
    failCount++;
}
finally
{
    process?.Kill();
    process?.WaitForExit();
}

// Summary
Console.WriteLine("\n=== Summary ===");
Console.WriteLine($"Pass: {passCount}");
Console.WriteLine($"Fail: {failCount}");

Environment.Exit(failCount > 0 ? 1 : 0);

// ============ In-Memory Repository for Testing ============
class InMemoryRepository : ILoanRepository
{
    private readonly Dictionary<string, Book> _books = new();
    private readonly Dictionary<string, Member> _members = new();
    private readonly Dictionary<string, LoanDto> _loans = new();

    public void SaveBook(Book book) => _books[book.Id] = book;
    public Book? GetBook(string id) => _books.TryGetValue(id, out var b) ? b with { AvailableCopies = b.Copies - GetActiveLoanCountForBook(id) } : null;

    public void SaveMember(Member member) => _members[member.Id] = member;
    public Member? GetMember(string id) => _members.TryGetValue(id, out var m) ? m : null;

    public void SaveLoan(LoanDto loan) => _loans[loan.Id] = loan;
    public LoanDto? GetLoan(string id) => _loans.TryGetValue(id, out var l) ? l : null;

    public List<LoanDto> GetLoansByMember(string memberId) => _loans.Values.Where(l => l.MemberId == memberId).ToList();

    public int GetActiveLoanCountForBook(string bookId) => _loans.Values.Count(l => l.BookId == bookId && l.Status == "active");
    public int GetActiveLoanCountForMember(string memberId) => _loans.Values.Count(l => l.MemberId == memberId && l.Status == "active");
}
