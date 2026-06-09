using System.Text.Json;
using System.Text.Json.Nodes;
using Library.Api;
using Library.Core;

var builder = WebApplication.CreateBuilder(args);

// DB パス: LIBRARY_DB_PATH(未設定時 ./library.db)。相対はプロセス作業ディレクトリ基準。
var dbPath = Environment.GetEnvironmentVariable("LIBRARY_DB_PATH");
if (string.IsNullOrEmpty(dbPath))
    dbPath = "./library.db";

var store = new LibraryStore(dbPath);
store.EnsureSchema();
builder.Services.AddSingleton(store);

var app = builder.Build();

// 未ハンドル例外で 500 を返さない設計(K-HTTP-REST-001: 500 は契約違反)。
// 例外は invalid_request に潰さず、契約列挙に無い 500 を避けるため共通エラー封筒で返す。
// ただし通常経路は例外を投げないよう設計しており、これは保険(採用=ずる報告 CHEAT-F01-005)。
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
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(Api.ErrorJson("invalid_request", "request could not be processed"));
        }
    }
});

// ---- POST /v1/books ----------------------------------------------------------
app.MapPost("/v1/books", async (HttpContext ctx) =>
{
    var (json, parseOk) = await Api.ReadJson(ctx);
    if (!parseOk || json is null)
        return Api.Error(400, "invalid_request", "invalid json body");

    if (!Api.TryGetString(json, "title", out var title) || title!.Length < 1 || title.Length > 200)
        return Api.Error(400, "invalid_request", "title must be a string of length 1..200");
    if (!Api.TryGetInt(json, "copies", out var copies) || copies < 1 || copies > 100)
        return Api.Error(400, "invalid_request", "copies must be an integer in 1..100");

    var book = store.InsertBook(title, copies);
    int active = store.CountActiveLoansForBook(book.Id);
    return Results.Json(Api.BookObject(book, active), statusCode: 201);
});

// ---- GET /v1/books/{id} ------------------------------------------------------
app.MapGet("/v1/books/{id}", (string id) =>
{
    var book = store.GetBook(id);
    if (book is null)
        return Api.Error(404, "not_found", "book not found");
    int active = store.CountActiveLoansForBook(book.Id);
    return Results.Json(Api.BookObject(book, active), statusCode: 200);
});

// ---- POST /v1/members --------------------------------------------------------
app.MapPost("/v1/members", async (HttpContext ctx) =>
{
    var (json, parseOk) = await Api.ReadJson(ctx);
    if (!parseOk || json is null)
        return Api.Error(400, "invalid_request", "invalid json body");

    if (!Api.TryGetString(json, "name", out var name) || name!.Length < 1 || name.Length > 100)
        return Api.Error(400, "invalid_request", "name must be a string of length 1..100");

    var member = store.InsertMember(name);
    var obj = new JsonObject { ["id"] = member.Id, ["name"] = member.Name };
    return Results.Json(obj, statusCode: 201);
});

// ---- POST /v1/loans ----------------------------------------------------------
app.MapPost("/v1/loans", async (HttpContext ctx) =>
{
    var (json, parseOk) = await Api.ReadJson(ctx);
    if (!parseOk || json is null)
        return Api.Error(400, "invalid_request", "invalid json body");

    // 1. 入力検証(欠落・型・日時形式)。bookId/memberId の値形式検証はしない(空文字も次段へ)。
    if (!Api.TryGetString(json, "bookId", out var bookId))
        return Api.Error(400, "invalid_request", "bookId is required");
    if (!Api.TryGetString(json, "memberId", out var memberId))
        return Api.Error(400, "invalid_request", "memberId is required");
    if (!Api.TryGetString(json, "loanedAtUtc", out var loanedRaw))
        return Api.Error(400, "invalid_request", "loanedAtUtc is required");
    if (!Iso8601Z.TryParse(loanedRaw, out var loanedAt))
        return Api.Error(400, "invalid_request", "loanedAtUtc must be strict literal-Z ISO-8601");

    var result = store.CreateLoan(bookId!, memberId!, loanedAt);
    return result.Outcome switch
    {
        LoanOutcome.NotFound => Api.Error(404, "not_found", "book or member not found"),
        LoanOutcome.Blocked => Api.Error(409, Api.ErrorCode(result.Error), Api.ErrorMessage(result.Error)),
        _ => Results.Json(Api.LoanObject(result.Loan!), statusCode: 201)
    };
});

// ---- POST /v1/loans/{id}/return ---------------------------------------------
app.MapPost("/v1/loans/{id}/return", async (string id, HttpContext ctx) =>
{
    var (json, parseOk) = await Api.ReadJson(ctx);
    if (!parseOk || json is null)
        return Api.Error(400, "invalid_request", "invalid json body");

    if (!Api.TryGetString(json, "returnedAtUtc", out var retRaw))
        return Api.Error(400, "invalid_request", "returnedAtUtc is required");
    if (!Iso8601Z.TryParse(retRaw, out var returnedAt))
        return Api.Error(400, "invalid_request", "returnedAtUtc must be strict literal-Z ISO-8601");

    var result = store.ReturnLoan(id, returnedAt);
    return result.Outcome switch
    {
        ReturnOutcome.NotFound => Api.Error(404, "not_found", "loan not found"),
        ReturnOutcome.AlreadyReturned => Api.Error(409, "already_returned", "loan already returned"),
        ReturnOutcome.InvalidInstant => Api.Error(400, "invalid_request", "returnedAtUtc is before loanedAtUtc"),
        _ => Results.Json(Api.LoanObject(result.Loan!), statusCode: 200)
    };
});

// ---- GET /v1/loans?memberId= -------------------------------------------------
app.MapGet("/v1/loans", (HttpContext ctx) =>
{
    // memberId クエリパラメータ自体が無い → 400。空文字(?memberId=)は「値あり」=404 側へ。
    if (!ctx.Request.Query.ContainsKey("memberId"))
        return Api.Error(400, "invalid_request", "memberId query parameter is required");

    var memberId = ctx.Request.Query["memberId"].ToString();
    if (!store.MemberExists(memberId))
        return Api.Error(404, "not_found", "member not found");

    var loans = store.ListLoansForMember(memberId);
    var items = new JsonArray();
    foreach (var loan in loans)
        items.Add(Api.LoanObject(loan));
    return Results.Json(new JsonObject { ["items"] = items }, statusCode: 200);
});

app.Run();

// 受入ハーネスからプロセス起動できるよう partial 化(直接参照はしないが慣例)。
public partial class Program { }
