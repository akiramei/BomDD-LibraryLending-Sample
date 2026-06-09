using System.Text.Json;
using System.Text.Json.Serialization;
using Library.Api;
using Library.Core;

var builder = WebApplication.CreateBuilder(args);

// JSON: camelCase, no null fields for active loans handled manually
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Database singleton
var dbPath = Environment.GetEnvironmentVariable("LIBRARY_DB_PATH") ?? "./library.db";
var db = new Database(dbPath);
db.EnsureSchema();
builder.Services.AddSingleton(db);

var app = builder.Build();

// ── Global exception handler (CHEAT-F01-006: defensive middleware) ──────────
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async ctx =>
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(
            """{"error":{"code":"internal_error","message":"An unexpected error occurred."}}""");
    });
});

// ── POST /v1/books ────────────────────────────────────────────────────────────
app.MapPost("/v1/books", (Database database, CreateBookRequest req) =>
{
    // Validation
    if (req.Title is null || req.Title.Length < 1 || req.Title.Length > 200)
        return ErrorResult(400, "invalid_request", "title must be 1..200 characters.");
    if (req.Copies is null || req.Copies < 1 || req.Copies > 100)
        return ErrorResult(400, "invalid_request", "copies must be 1..100.");

    var id = IdGenerator.NewBookId();
    var book = database.InsertBook(id, req.Title, req.Copies.Value);

    return Results.Json(new
    {
        id = book.Id,
        title = book.Title,
        copies = book.Copies,
        availableCopies = book.Copies  // just created, no active loans
    }, statusCode: 201);
});

// ── GET /v1/books/{id} ────────────────────────────────────────────────────────
app.MapGet("/v1/books/{id}", (Database database, string id) =>
{
    var book = database.GetBook(id);
    if (book == null)
        return ErrorResult(404, "not_found", "Book not found.");

    var activeCount = database.GetActiveLoansCount(id);
    var available = book.Copies - activeCount;

    return Results.Json(new
    {
        id = book.Id,
        title = book.Title,
        copies = book.Copies,
        availableCopies = available
    });
});

// ── POST /v1/members ──────────────────────────────────────────────────────────
app.MapPost("/v1/members", (Database database, CreateMemberRequest req) =>
{
    if (req.Name is null || req.Name.Length < 1 || req.Name.Length > 100)
        return ErrorResult(400, "invalid_request", "name must be 1..100 characters.");

    var id = IdGenerator.NewMemberId();
    var member = database.InsertMember(id, req.Name);

    return Results.Json(new { id = member.Id, name = member.Name }, statusCode: 201);
});

// ── POST /v1/loans ────────────────────────────────────────────────────────────
app.MapPost("/v1/loans", (Database database, CreateLoanRequest req) =>
{
    // Step 1: input validation
    // bookId/memberId: must be present (non-null), but no format validation on value
    if (req.BookId is null)
        return ErrorResult(400, "invalid_request", "bookId is required.");
    if (req.MemberId is null)
        return ErrorResult(400, "invalid_request", "memberId is required.");
    if (req.LoanedAtUtc is null)
        return ErrorResult(400, "invalid_request", "loanedAtUtc is required.");

    var loanedAt = DateTimeParser.TryParse(req.LoanedAtUtc);
    if (loanedAt == null)
        return ErrorResult(400, "invalid_request", "loanedAtUtc must be a valid UTC ISO-8601 datetime with uppercase Z.");

    var (loan, error) = database.TryCreateLoan(req.BookId, req.MemberId, loanedAt.Value);

    if (error != null)
        return MapDomainError(error);

    return Results.Json(new
    {
        id = loan!.Id,
        bookId = loan.BookId,
        memberId = loan.MemberId,
        loanedAtUtc = DateTimeParser.Format(loan.LoanedAtUtc),
        dueDateUtc = loan.DueDateUtc.ToString("yyyy-MM-dd"),
        status = "active"
    }, statusCode: 201);
});

// ── POST /v1/loans/{id}/return ────────────────────────────────────────────────
app.MapPost("/v1/loans/{id}/return", (Database database, string id, ReturnLoanRequest req) =>
{
    // Step 1: input validation
    if (req.ReturnedAtUtc is null)
        return ErrorResult(400, "invalid_request", "returnedAtUtc is required.");

    var returnedAt = DateTimeParser.TryParse(req.ReturnedAtUtc);
    if (returnedAt == null)
        return ErrorResult(400, "invalid_request", "returnedAtUtc must be a valid UTC ISO-8601 datetime with uppercase Z.");

    // Steps 2-4 are handled inside TryReturnLoan
    var (loan, error) = database.TryReturnLoan(id, returnedAt.Value);

    if (error != null)
        return MapDomainError(error);

    return Results.Json(new
    {
        id = loan!.Id,
        bookId = loan.BookId,
        memberId = loan.MemberId,
        loanedAtUtc = DateTimeParser.Format(loan.LoanedAtUtc),
        dueDateUtc = loan.DueDateUtc.ToString("yyyy-MM-dd"),
        status = "returned",
        returnedAtUtc = DateTimeParser.Format(loan.ReturnedAtUtc!.Value),
        fineAmount = loan.FineAmount!.Value
    });
});

// ── GET /v1/loans?memberId={id} ───────────────────────────────────────────────
app.MapGet("/v1/loans", (Database database, HttpRequest httpReq) =>
{
    // memberId query param must be present (even empty string is valid as a value)
    if (!httpReq.Query.ContainsKey("memberId"))
        return ErrorResult(400, "invalid_request", "memberId query parameter is required.");

    var memberId = httpReq.Query["memberId"].ToString();

    // Check member exists (empty string still goes to 404 path per spec)
    var member = database.GetMember(memberId);
    if (member == null)
        return ErrorResult(404, "not_found", "Member not found.");

    var loans = database.GetLoansForMember(memberId);

    // Sort: loanedAtUtc instant ascending, then id ordinal ascending
    var sorted = loans
        .OrderBy(l => l.LoanedAtUtc)
        .ThenBy(l => l.Id, StringComparer.Ordinal)
        .ToList();

    var items = sorted.Select(l =>
    {
        if (l.Status == LoanStatus.Active)
        {
            return (object)new
            {
                id = l.Id,
                bookId = l.BookId,
                memberId = l.MemberId,
                loanedAtUtc = DateTimeParser.Format(l.LoanedAtUtc),
                dueDateUtc = l.DueDateUtc.ToString("yyyy-MM-dd"),
                status = "active"
            };
        }
        else
        {
            return (object)new
            {
                id = l.Id,
                bookId = l.BookId,
                memberId = l.MemberId,
                loanedAtUtc = DateTimeParser.Format(l.LoanedAtUtc),
                dueDateUtc = l.DueDateUtc.ToString("yyyy-MM-dd"),
                status = "returned",
                returnedAtUtc = DateTimeParser.Format(l.ReturnedAtUtc!.Value),
                fineAmount = l.FineAmount!.Value
            };
        }
    }).ToList();

    return Results.Json(new { items });
});

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

static IResult ErrorResult(int status, string code, string message) =>
    Results.Json(new { error = new { code, message } }, statusCode: status);

static IResult MapDomainError(DomainError error) => error switch
{
    DomainError.NotFound e           => ErrorResult(404, "not_found", e.Message),
    DomainError.InvalidRequest e     => ErrorResult(400, "invalid_request", e.Message),
    DomainError.NoCopiesAvailable    => ErrorResult(409, "no_copies_available", "No copies available."),
    DomainError.LoanLimitExceeded    => ErrorResult(409, "loan_limit_exceeded", "Member has reached the active loan limit."),
    DomainError.MemberOverdueBlocked => ErrorResult(409, "member_overdue_blocked", "Member has overdue loans."),
    DomainError.AlreadyReturned      => ErrorResult(409, "already_returned", "Loan has already been returned."),
    _                                => ErrorResult(500, "internal_error", "Unknown error.")
};

// ── Request DTOs ──────────────────────────────────────────────────────────────

record CreateBookRequest(string? Title, int? Copies);
record CreateMemberRequest(string? Name);
record CreateLoanRequest(string? BookId, string? MemberId, string? LoanedAtUtc);
record ReturnLoanRequest(string? ReturnedAtUtc);
