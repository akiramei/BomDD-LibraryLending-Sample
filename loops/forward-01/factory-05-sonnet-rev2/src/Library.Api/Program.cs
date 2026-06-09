using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Library.Api;
using Library.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ── JSON serialization: camelCase, omit null ─────────────────────────────
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ── Database singleton ────────────────────────────────────────────────────
var dbPath = Environment.GetEnvironmentVariable("LIBRARY_DB_PATH") ?? "./library.db";
var db = new Database(dbPath);
builder.Services.AddSingleton(db);

var app = builder.Build();

// ── Global exception handler (CHEAT-F01-001) ─────────────────────────────
app.Use(async (ctx, next) =>
{
    try
    {
        await next(ctx);
    }
    catch (Exception)
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new
        {
            error = new { code = "internal_error", message = "An unexpected error occurred." }
        });
        await ctx.Response.WriteAsync(body);
    }
});

// ── Shared options for JsonDocument parsing ───────────────────────────────
var jsonDocOpts = new JsonDocumentOptions { AllowTrailingCommas = false };

// ── Helper: error response ────────────────────────────────────────────────
IResult ErrorResult(int status, string code, string message)
{
    return Results.Json(new { error = new { code, message } }, statusCode: status);
}

// ── Helper: validate non-blank string ────────────────────────────────────
static bool IsBlank(string? s)
    => string.IsNullOrEmpty(s) || s.Trim().Length == 0;

// ── Helper: generate ID (INV-5) ───────────────────────────────────────────
static string NewId(string prefix)
    => prefix + Guid.NewGuid().ToString("N");

// ── Helper: read request body as JsonDocument ────────────────────────────
static async Task<JsonDocument?> ReadBodyAsync(HttpContext ctx)
{
    try
    {
        return await JsonDocument.ParseAsync(ctx.Request.Body);
    }
    catch
    {
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 2.1 POST /v1/books
// ─────────────────────────────────────────────────────────────────────────
app.MapPost("/v1/books", async (Database db, HttpContext ctx) =>
{
    using var doc = await ReadBodyAsync(ctx);
    if (doc is null)
        return ErrorResult(400, "invalid_request", "Invalid JSON body.");

    var root = doc.RootElement;

    if (!root.TryGetProperty("title", out var titleEl) || titleEl.ValueKind != JsonValueKind.String)
        return ErrorResult(400, "invalid_request", "title is required and must be a string.");
    string title = titleEl.GetString()!;
    if (title.Length < 1 || title.Length > 200)
        return ErrorResult(400, "invalid_request", "title must be 1..200 characters.");

    if (!root.TryGetProperty("copies", out var copiesEl) || copiesEl.ValueKind != JsonValueKind.Number)
        return ErrorResult(400, "invalid_request", "copies is required and must be an integer.");
    if (!copiesEl.TryGetInt32(out int copies) || copies < 1 || copies > 100)
        return ErrorResult(400, "invalid_request", "copies must be 1..100.");

    var id = NewId("bk_");
    db.InsertBook(id, title, copies);

    return Results.Json(new { id, title, copies, availableCopies = copies }, statusCode: 201);
});

// ─────────────────────────────────────────────────────────────────────────
// 2.2 GET /v1/books/{id}
// ─────────────────────────────────────────────────────────────────────────
app.MapGet("/v1/books/{id}", (Database db, string id) =>
{
    var book = db.GetBook(id);
    if (book is null)
        return ErrorResult(404, "not_found", "Book not found.");

    int active = db.GetActiveLoansForBook(id);
    int available = book.Copies - active;
    return Results.Json(new
    {
        id = book.Id,
        title = book.Title,
        copies = book.Copies,
        availableCopies = available
    });
});

// ─────────────────────────────────────────────────────────────────────────
// 2.3 POST /v1/members
// ─────────────────────────────────────────────────────────────────────────
app.MapPost("/v1/members", async (Database db, HttpContext ctx) =>
{
    using var doc = await ReadBodyAsync(ctx);
    if (doc is null)
        return ErrorResult(400, "invalid_request", "Invalid JSON body.");

    var root = doc.RootElement;

    if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
        return ErrorResult(400, "invalid_request", "name is required and must be a string.");
    string name = nameEl.GetString()!;
    if (name.Length < 1 || name.Length > 100)
        return ErrorResult(400, "invalid_request", "name must be 1..100 characters.");

    var id = NewId("mb_");
    db.InsertMember(id, name);

    return Results.Json(new { id, name }, statusCode: 201);
});

// ─────────────────────────────────────────────────────────────────────────
// 2.4 POST /v1/loans
// ─────────────────────────────────────────────────────────────────────────
app.MapPost("/v1/loans", async (Database db, HttpContext ctx) =>
{
    using var doc = await ReadBodyAsync(ctx);
    if (doc is null)
        return ErrorResult(400, "invalid_request", "Invalid JSON body.");

    var root = doc.RootElement;

    // Step 1: input validation (rev2: blank/whitespace → 400, same as missing)
    if (!root.TryGetProperty("bookId", out var bookIdEl) || bookIdEl.ValueKind != JsonValueKind.String)
        return ErrorResult(400, "invalid_request", "bookId is required and must be a non-empty string.");
    string bookId = bookIdEl.GetString()!;
    if (IsBlank(bookId))
        return ErrorResult(400, "invalid_request", "bookId must not be blank.");

    if (!root.TryGetProperty("memberId", out var memberIdEl) || memberIdEl.ValueKind != JsonValueKind.String)
        return ErrorResult(400, "invalid_request", "memberId is required and must be a non-empty string.");
    string memberId = memberIdEl.GetString()!;
    if (IsBlank(memberId))
        return ErrorResult(400, "invalid_request", "memberId must not be blank.");

    if (!root.TryGetProperty("loanedAtUtc", out var loanedEl) || loanedEl.ValueKind != JsonValueKind.String)
        return ErrorResult(400, "invalid_request", "loanedAtUtc is required and must be a string.");
    string loanedRaw = loanedEl.GetString()!;
    if (!DateTimeParser.TryParse(loanedRaw, out var loanedAt))
        return ErrorResult(400, "invalid_request", "loanedAtUtc must be yyyy-MM-ddTHH:mm:ssZ (uppercase Z only).");

    // Step 2: entity existence
    if (db.GetBook(bookId) is null)
        return ErrorResult(404, "not_found", "Book not found.");
    if (db.GetMember(memberId) is null)
        return ErrorResult(404, "not_found", "Member not found.");

    // Steps 3-5 + insert (atomic in DB layer)
    var loanId = NewId("ln_");
    var dueDate = LendingDomain.ComputeDueDate(loanedAt);
    var loanedStr = DateTimeParser.Format(loanedAt);
    var dueDateStr = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    var (errCode, _) = db.TryInsertLoan(loanId, bookId, memberId, loanedStr, dueDateStr, loanedAt);

    return errCode switch
    {
        "member_overdue_blocked" => ErrorResult(409, "member_overdue_blocked", "Member has overdue loans."),
        "loan_limit_exceeded"    => ErrorResult(409, "loan_limit_exceeded", "Member has reached the active loan limit."),
        "no_copies_available"    => ErrorResult(409, "no_copies_available", "No copies available."),
        _ => Results.Json(new
        {
            id = loanId,
            bookId,
            memberId,
            loanedAtUtc = loanedStr,
            dueDateUtc = dueDateStr,
            status = "active"
        }, statusCode: 201)
    };
});

// ─────────────────────────────────────────────────────────────────────────
// 2.5 POST /v1/loans/{id}/return
// ─────────────────────────────────────────────────────────────────────────
app.MapPost("/v1/loans/{id}/return", async (Database db, string id, HttpContext ctx) =>
{
    using var doc = await ReadBodyAsync(ctx);
    if (doc is null)
        return ErrorResult(400, "invalid_request", "Invalid JSON body.");

    var root = doc.RootElement;

    // Step 1: input validation
    if (!root.TryGetProperty("returnedAtUtc", out var retEl) || retEl.ValueKind != JsonValueKind.String)
        return ErrorResult(400, "invalid_request", "returnedAtUtc is required and must be a string.");
    string retRaw = retEl.GetString()!;
    if (!DateTimeParser.TryParse(retRaw, out var returnedAt))
        return ErrorResult(400, "invalid_request", "returnedAtUtc must be yyyy-MM-ddTHH:mm:ssZ (uppercase Z only).");

    // Step 2: loan existence
    var loan = db.GetLoan(id);
    if (loan is null)
        return ErrorResult(404, "not_found", "Loan not found.");

    // Step 3: already returned
    if (loan.Status == "returned")
        return ErrorResult(409, "already_returned", "This loan has already been returned.");

    // Step 4: returnedAt < loanedAt (instant/tick comparison)
    DateTimeParser.TryParse(loan.LoanedAtUtc, out var loanedAt);
    if (returnedAt < loanedAt)
        return ErrorResult(400, "invalid_request", "returnedAtUtc must not be before loanedAtUtc.");

    // Calculate fine
    var dueDate = DateOnly.Parse(loan.DueDateUtc, CultureInfo.InvariantCulture);
    int fine = LendingDomain.ComputeFine(returnedAt, dueDate);
    var returnedStr = DateTimeParser.Format(returnedAt);

    var (errCode, updated) = db.TryReturnLoan(id, returnedStr, fine);

    return errCode switch
    {
        "not_found"       => ErrorResult(404, "not_found", "Loan not found."),
        "already_returned"=> ErrorResult(409, "already_returned", "This loan has already been returned."),
        _ => Results.Json(new
        {
            id = updated!.Id,
            bookId = updated.BookId,
            memberId = updated.MemberId,
            loanedAtUtc = updated.LoanedAtUtc,
            dueDateUtc = updated.DueDateUtc,
            status = updated.Status,
            returnedAtUtc = returnedStr,
            fineAmount = fine
        })
    };
});

// ─────────────────────────────────────────────────────────────────────────
// 2.6 GET /v1/loans?memberId=
// ─────────────────────────────────────────────────────────────────────────
app.MapGet("/v1/loans", (Database db, HttpContext ctx) =>
{
    // rev2: missing or blank memberId → 400
    if (!ctx.Request.Query.ContainsKey("memberId"))
        return ErrorResult(400, "invalid_request", "memberId query parameter is required.");
    string memberId = ctx.Request.Query["memberId"].ToString();
    if (IsBlank(memberId))
        return ErrorResult(400, "invalid_request", "memberId must not be blank.");

    if (db.GetMember(memberId) is null)
        return ErrorResult(404, "not_found", "Member not found.");

    var loans = db.GetLoansForMember(memberId);

    // active loans omit returnedAtUtc/fineAmount (K-JSON-001)
    var items = loans.Select<LoanRow, object>(l =>
        l.Status == "active"
            ? new { id = l.Id, bookId = l.BookId, memberId = l.MemberId,
                    loanedAtUtc = l.LoanedAtUtc, dueDateUtc = l.DueDateUtc, status = l.Status }
            : new { id = l.Id, bookId = l.BookId, memberId = l.MemberId,
                    loanedAtUtc = l.LoanedAtUtc, dueDateUtc = l.DueDateUtc, status = l.Status,
                    returnedAtUtc = l.ReturnedAtUtc, fineAmount = l.FineAmount }
    ).ToList();

    return Results.Json(new { items });
});

app.Run();
