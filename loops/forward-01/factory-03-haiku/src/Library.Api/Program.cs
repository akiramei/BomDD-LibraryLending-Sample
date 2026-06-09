using Library.Api.Middleware;
using Library.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddScoped<ApiService>();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
    db.Initialize();
}

// Error handling middleware
app.UseMiddleware<ErrorHandlingMiddleware>();

// Configure routes per K-HTTP-REST-001
var group = app.MapGroup("/v1");

// Books endpoints
group.MapPost("/books", async (CreateBookRequest request, ApiService api) =>
{
    return await api.CreateBook(request);
}).WithName("CreateBook");

group.MapGet("/books/{id}", async (string id, ApiService api) =>
{
    return await api.GetBook(id);
}).WithName("GetBook");

// Members endpoints
group.MapPost("/members", async (CreateMemberRequest request, ApiService api) =>
{
    return await api.CreateMember(request);
}).WithName("CreateMember");

// Loans endpoints
group.MapPost("/loans", async (CreateLoanRequest request, ApiService api) =>
{
    return await api.CreateLoan(request);
}).WithName("CreateLoan");

group.MapPost("/loans/{id}/return", async (string id, ReturnLoanRequest request, ApiService api) =>
{
    return await api.ReturnLoan(id, request);
}).WithName("ReturnLoan");

group.MapGet("/loans", async (string? memberId, ApiService api) =>
{
    return await api.ListLoans(memberId);
}).WithName("ListLoans");

await app.RunAsync();

// Request/Response DTOs
public class CreateBookRequest
{
    public string? Title { get; set; }
    public int Copies { get; set; }
}

public class BookResponse
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Copies { get; set; }
    public int AvailableCopies { get; set; }
}

public class CreateMemberRequest
{
    public string? Name { get; set; }
}

public class MemberResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class CreateLoanRequest
{
    public string? BookId { get; set; }
    public string? MemberId { get; set; }
    public string? LoanedAtUtc { get; set; }
}

public class CreateLoanResponse
{
    public string Id { get; set; } = string.Empty;
    public string BookId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string LoanedAtUtc { get; set; } = string.Empty;
    public string DueDateUtc { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
}

public class ReturnLoanRequest
{
    public string? ReturnedAtUtc { get; set; }
}

public class ReturnLoanResponse
{
    public string Id { get; set; } = string.Empty;
    public string BookId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string LoanedAtUtc { get; set; } = string.Empty;
    public string DueDateUtc { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ReturnedAtUtc { get; set; } = string.Empty;
    public int FineAmount { get; set; }
}

public class ListLoansResponse
{
    public List<object> Items { get; set; } = new();
}

public class ErrorResponse
{
    public ErrorDetail Error { get; set; } = new();
}

public class ErrorDetail
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
