using System.Text.Json;
using Library.Api;
using Library.Core;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Store as singleton (stateless connection factory). Schema created at startup.
var store = new Store(Store.ResolveDbPath());
store.EnsureSchema();
builder.Services.AddSingleton(store);

var app = builder.Build();

// Global exception guard: never emit a bare 500. Map unhandled errors to the common
// envelope + internal_error (spec §2.8 / work order). 500 occurrence itself is an acceptance fail.
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch
    {
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(Api.SerializeError(ErrorCodes.InternalError, "internal error"));
        }
    }
});

// ---- POST /v1/books -------------------------------------------------------
app.MapPost("/v1/books", async (HttpContext http) =>
{
    var (ok, root, err) = await Api.ReadJsonObject(http);
    if (!ok) return err!;

    if (!Api.TryGetString(root, "title", out var title) || title!.Length is < 1 or > 200)
        return Api.Invalid("title is required and must be 1..200 chars");
    if (!Api.TryGetInt(root, "copies", out var copies) || copies is < 1 or > 100)
        return Api.Invalid("copies is required and must be an integer 1..100");

    var id = Ids.Book();
    store.InsertBook(id, title!, copies);
    return Results.Json(new
    {
        id,
        title,
        copies,
        availableCopies = copies // fresh book => no active loans
    }, statusCode: StatusCodes.Status201Created);
});

// ---- GET /v1/books/{id} ---------------------------------------------------
app.MapGet("/v1/books/{id}", (string id, Store s) =>
{
    var book = s.GetBook(id);
    if (book is null) return Api.NotFound();
    var available = s.GetBookAvailableCopies(id) ?? book.Copies;
    return Results.Json(new
    {
        id = book.Id,
        title = book.Title,
        copies = book.Copies,
        availableCopies = available
    }, statusCode: StatusCodes.Status200OK);
});

// ---- POST /v1/members -----------------------------------------------------
app.MapPost("/v1/members", async (HttpContext http) =>
{
    var (ok, root, err) = await Api.ReadJsonObject(http);
    if (!ok) return err!;

    if (!Api.TryGetString(root, "name", out var name) || name!.Length is < 1 or > 100)
        return Api.Invalid("name is required and must be 1..100 chars");

    // memberType: optional, default "standard". Only "standard"|"premium" accepted (REQ-008 / §2.3 rev3).
    string memberType = "standard";
    if (root.TryGetProperty("memberType", out var mtProp))
    {
        if (mtProp.ValueKind != System.Text.Json.JsonValueKind.String)
            return Api.Invalid("memberType must be 'standard' or 'premium'");
        var mt = mtProp.GetString();
        if (mt != "standard" && mt != "premium")
            return Api.Invalid("memberType must be 'standard' or 'premium'");
        memberType = mt;
    }

    var id = Ids.Member();
    store.InsertMember(id, name!, memberType);
    return Results.Json(new { id, name, memberType }, statusCode: StatusCodes.Status201Created);
});

// ---- POST /v1/loans -------------------------------------------------------
app.MapPost("/v1/loans", async (HttpContext http) =>
{
    var (ok, root, err) = await Api.ReadJsonObject(http);
    if (!ok) return err!;

    // 1. input validation: bookId/memberId non-empty strings; loanedAtUtc strict literal-Z.
    if (!Api.TryGetNonEmptyString(root, "bookId", out var bookId))
        return Api.Invalid("bookId is required and must be a non-empty string");
    if (!Api.TryGetNonEmptyString(root, "memberId", out var memberId))
        return Api.Invalid("memberId is required and must be a non-empty string");
    if (!Api.TryGetString(root, "loanedAtUtc", out var loanedRaw) ||
        !UtcInstant.TryParse(loanedRaw, out var loanedAt))
        return Api.Invalid("loanedAtUtc is required and must be a strict literal-Z UTC instant");

    var loanedText = UtcInstant.Format(loanedAt);

    var result = store.CreateLoanAtomic(
        Ids.Loan(), bookId!, memberId!, loanedAt, loanedText,
        ctx =>
        {
            var decision = LendingDecisions.EvaluateLoan(ctx);
            var dueText = UtcInstant.FormatDate(LendingDomain.DueDate(ctx.LoanedAtUtc));
            return (decision, dueText);
        });

    return result.Decision switch
    {
        LoanDecision.Allowed => Results.Json(Api.LoanJson(result.Row!), statusCode: StatusCodes.Status201Created),
        LoanDecision.NotFound => Api.NotFound(),
        LoanDecision.MemberOverdueBlocked => Api.Conflict(ErrorCodes.MemberOverdueBlocked, "member has an overdue active loan"),
        LoanDecision.LoanLimitExceeded => Api.Conflict(ErrorCodes.LoanLimitExceeded, "member active loan limit reached"),
        LoanDecision.NoCopiesAvailable => Api.Conflict(ErrorCodes.NoCopiesAvailable, "no copies available"),
        _ => Api.Internal()
    };
});

// ---- POST /v1/loans/{id}/return -------------------------------------------
app.MapPost("/v1/loans/{id}/return", async (string id, HttpContext http) =>
{
    var (ok, root, err) = await Api.ReadJsonObject(http);
    if (!ok) return err!;

    if (!Api.TryGetString(root, "returnedAtUtc", out var returnedRaw) ||
        !UtcInstant.TryParse(returnedRaw, out var returnedAt))
        return Api.Invalid("returnedAtUtc is required and must be a strict literal-Z UTC instant");

    var returnedText = UtcInstant.Format(returnedAt);

    var result = store.ReturnLoanAtomic(
        id, returnedAt, returnedText,
        loan =>
        {
            // loaned/dueDate are persisted as canonical strings; re-parse for decision/fine.
            var loanedAt = DateTimeOffset.ParseExact(
                loan.LoanedAtUtc, UtcInstant.OutputFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
            var decision = LendingDecisions.EvaluateReturn(
                loanExists: true,
                alreadyReturned: loan.Status == "returned",
                loanedAtUtc: loanedAt,
                returnedAtUtc: returnedAt);
            int fine = 0;
            if (decision == ReturnDecision.Allowed)
            {
                var due = DateOnly.ParseExact(loan.DueDateUtc, "yyyy-MM-dd");
                fine = LendingDomain.Fine(returnedAt, due);
            }
            return (decision, fine);
        });

    return result.Decision switch
    {
        ReturnDecision.Allowed => Results.Json(Api.LoanJson(result.Row!), statusCode: StatusCodes.Status200OK),
        ReturnDecision.NotFound => Api.NotFound(),
        ReturnDecision.AlreadyReturned => Api.Conflict(ErrorCodes.AlreadyReturned, "loan already returned"),
        ReturnDecision.ReturnBeforeLoan => Api.Invalid("returnedAtUtc must not be before loanedAtUtc"),
        _ => Api.Internal()
    };
});

// ---- GET /v1/loans?memberId= ----------------------------------------------
app.MapGet("/v1/loans", (HttpContext http, Store s) =>
{
    // memberId query param must be present and a non-empty/non-whitespace value.
    if (!http.Request.Query.TryGetValue("memberId", out var raw))
        return Api.Invalid("memberId query parameter is required");
    var memberId = raw.ToString();
    if (string.IsNullOrWhiteSpace(memberId))
        return Api.Invalid("memberId must be a non-empty string");

    if (!s.MemberExists(memberId))
        return Api.NotFound();

    var loans = s.GetLoansByMember(memberId);

    // Order: loanedAtUtc instant ascending; tie => id ordinal ascending.
    var ordered = loans
        .OrderBy(l => DateTimeOffset.ParseExact(
            l.LoanedAtUtc, UtcInstant.OutputFormat, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal))
        .ThenBy(l => l.Id, StringComparer.Ordinal)
        .Select(Api.LoanJson)
        .ToList();

    return Results.Json(new { items = ordered }, statusCode: StatusCodes.Status200OK);
});

app.Run();

// Exposed so the acceptance harness can launch this assembly as a subprocess.
public partial class Program { }
