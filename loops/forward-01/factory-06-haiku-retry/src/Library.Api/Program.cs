using System.Text.Json;
using System.Text.Json.Serialization;
using Library.Api;
using Library.Core;

var builder = WebApplication.CreateBuilder(args);

// Get database path from environment or use default
var dbPath = Environment.GetEnvironmentVariable("LIBRARY_DB_PATH") ?? "./library.db";

// Register repository and service
builder.Services.AddSingleton<ILoanRepository>(new SqliteRepository(dbPath));
builder.Services.AddSingleton<LoanService>();

var app = builder.Build();

// Configure JSON serialization
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
};

// Helper to write error response
void WriteErrorResponse(HttpContext ctx, int status, string code, string message)
{
    ctx.Response.StatusCode = status;
    ctx.Response.ContentType = "application/json";
    var response = new { error = new { code, message } };
    ctx.Response.WriteAsJsonAsync(response, jsonOptions).Wait();
}

// Helper to write JSON response
void WriteJsonResponse(HttpContext ctx, int status, object data)
{
    ctx.Response.StatusCode = status;
    ctx.Response.ContentType = "application/json";
    ctx.Response.WriteAsJsonAsync(data, jsonOptions).Wait();
}

// GET /v1/books/{id}
app.MapGet("/v1/books/{id}", (string id, ILoanRepository repo, HttpContext ctx) =>
{
    var book = repo.GetBook(id);
    if (book == null)
    {
        WriteErrorResponse(ctx, 404, "not_found", "Book not found");
        return;
    }

    WriteJsonResponse(ctx, 200, book);
});

// POST /v1/books
app.MapPost("/v1/books", async (HttpContext ctx, LoanService service) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var jsonStr = await reader.ReadToEndAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(jsonStr);

        if (!json.TryGetProperty("title", out var titleProp) || titleProp.ValueKind != JsonValueKind.String)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Missing or invalid title");
            return;
        }

        if (!json.TryGetProperty("copies", out var copiesProp) || copiesProp.ValueKind != JsonValueKind.Number)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Missing or invalid copies");
            return;
        }

        var title = titleProp.GetString() ?? "";
        var copies = copiesProp.GetInt32();

        if (string.IsNullOrEmpty(title) || title.Length < 1 || title.Length > 200)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Title must be 1-200 characters");
            return;
        }

        if (copies < 1 || copies > 100)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Copies must be 1-100");
            return;
        }

        var book = service.RegisterBook(title, copies);
        WriteJsonResponse(ctx, 201, book);
    }
    catch
    {
        WriteErrorResponse(ctx, 400, "invalid_request", "Invalid request");
    }
});

// POST /v1/members
app.MapPost("/v1/members", async (HttpContext ctx, LoanService service) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var jsonStr = await reader.ReadToEndAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(jsonStr);

        if (!json.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Missing or invalid name");
            return;
        }

        var name = nameProp.GetString() ?? "";

        if (string.IsNullOrEmpty(name) || name.Length < 1 || name.Length > 100)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Name must be 1-100 characters");
            return;
        }

        var member = service.RegisterMember(name);
        WriteJsonResponse(ctx, 201, member);
    }
    catch
    {
        WriteErrorResponse(ctx, 400, "invalid_request", "Invalid request");
    }
});

// POST /v1/loans
app.MapPost("/v1/loans", async (HttpContext ctx, LoanService service) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var jsonStr = await reader.ReadToEndAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(jsonStr);

        if (!json.TryGetProperty("bookId", out var bookIdProp) || bookIdProp.ValueKind != JsonValueKind.String)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Missing or invalid bookId");
            return;
        }

        if (!json.TryGetProperty("memberId", out var memberIdProp) || memberIdProp.ValueKind != JsonValueKind.String)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Missing or invalid memberId");
            return;
        }

        if (!json.TryGetProperty("loanedAtUtc", out var loanedAtProp) || loanedAtProp.ValueKind != JsonValueKind.String)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Missing or invalid loanedAtUtc");
            return;
        }

        var bookId = bookIdProp.GetString() ?? "";
        var memberId = memberIdProp.GetString() ?? "";
        var loanedAtUtc = loanedAtProp.GetString() ?? "";

        if (string.IsNullOrWhiteSpace(bookId) || string.IsNullOrWhiteSpace(memberId))
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "bookId and memberId must be non-empty");
            return;
        }

        var (loan, errorCode) = service.CreateLoan(bookId, memberId, loanedAtUtc);

        if (errorCode != null)
        {
            int statusCode = errorCode switch
            {
                "invalid_request" => 400,
                "not_found" => 404,
                "member_overdue_blocked" => 409,
                "loan_limit_exceeded" => 409,
                "no_copies_available" => 409,
                _ => 400
            };

            WriteErrorResponse(ctx, statusCode, errorCode, errorCode);
            return;
        }

        WriteJsonResponse(ctx, 201, loan);
    }
    catch
    {
        WriteErrorResponse(ctx, 400, "invalid_request", "Invalid request");
    }
});

// POST /v1/loans/{id}/return
app.MapPost("/v1/loans/{id}/return", async (HttpContext ctx, string id, LoanService service) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var jsonStr = await reader.ReadToEndAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(jsonStr);

        if (!json.TryGetProperty("returnedAtUtc", out var returnedAtProp) || returnedAtProp.ValueKind != JsonValueKind.String)
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "Missing or invalid returnedAtUtc");
            return;
        }

        var returnedAtUtc = returnedAtProp.GetString() ?? "";

        var (loan, errorCode) = service.ReturnLoan(id, returnedAtUtc);

        if (errorCode != null)
        {
            int statusCode = errorCode switch
            {
                "invalid_request" => 400,
                "not_found" => 404,
                "already_returned" => 409,
                _ => 400
            };

            WriteErrorResponse(ctx, statusCode, errorCode, errorCode);
            return;
        }

        WriteJsonResponse(ctx, 200, loan);
    }
    catch
    {
        WriteErrorResponse(ctx, 400, "invalid_request", "Invalid request");
    }
});

// GET /v1/loans?memberId={id}
app.MapGet("/v1/loans", (HttpContext ctx, LoanService service, ILoanRepository repo) =>
{
    try
    {
        var memberId = ctx.Request.Query["memberId"].ToString();

        if (string.IsNullOrWhiteSpace(memberId))
        {
            WriteErrorResponse(ctx, 400, "invalid_request", "memberId is required");
            return;
        }

        var member = repo.GetMember(memberId);
        if (member == null)
        {
            WriteErrorResponse(ctx, 404, "not_found", "Member not found");
            return;
        }

        var loans = service.GetLoansByMember(memberId);

        // Build items with conditional fields
        var items = loans.Select(l => (object)(l.Status == "active"
            ? new { l.Id, l.BookId, l.MemberId, l.LoanedAtUtc, l.DueDateUtc, l.Status }
            : new { l.Id, l.BookId, l.MemberId, l.LoanedAtUtc, l.DueDateUtc, l.Status, l.ReturnedAtUtc, l.FineAmount }
        )).ToList();

        WriteJsonResponse(ctx, 200, new { items });
    }
    catch
    {
        WriteErrorResponse(ctx, 400, "invalid_request", "Invalid request");
    }
});

app.Run();
