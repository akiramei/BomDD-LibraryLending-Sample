using Library.Core;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Library.Api.Services;

/// <summary>
/// API service implementing HTTP contract (M-API-SQLITE-001)
/// </summary>
public class ApiService
{
    private readonly DatabaseService _db;
    private readonly LendingService _lending = new();
    private static int _bookSequence = 1;
    private static int _memberSequence = 1;
    private static int _loanSequence = 1;

    public ApiService(DatabaseService db)
    {
        _db = db;
    }

    /// <summary>
    /// Validate and parse datetime per K-UTC-ISO8601-001
    /// Only accepts: yyyy-MM-ddTHH:mm:ssZ or with 1-7 fractional seconds
    /// Rejects: lowercase z, numeric offsets, missing offset, date-only
    /// </summary>
    private static (bool isValid, DateTime? parsed) ParseStrictDateTime(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return (false, null);

        // Must end with uppercase Z
        if (!input.EndsWith("Z"))
            return (false, null);

        // Check for numeric offset patterns (reject +09:00, +00:00, -05:00, etc.)
        if (input.Contains('+') || (input.Count(c => c == '-') > 2))
            return (false, null);

        var datetimePartWithZ = input;

        try
        {
            // Try parsing with exact format
            var formats = new[]
            {
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss.fZ",
                "yyyy-MM-ddTHH:mm:ss.ffZ",
                "yyyy-MM-ddTHH:mm:ss.fffZ",
                "yyyy-MM-ddTHH:mm:ss.ffffZ",
                "yyyy-MM-ddTHH:mm:ss.fffffZ",
                "yyyy-MM-ddTHH:mm:ss.ffffffZ",
                "yyyy-MM-ddTHH:mm:ss.fffffffZ"
            };

            if (DateTime.TryParseExact(datetimePartWithZ, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return (true, parsed);
            }

            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    public async Task<IResult> CreateBook(CreateBookRequest request)
    {
        // Validate input (§2.1)
        if (string.IsNullOrEmpty(request.Title) || request.Title.Length < 1 || request.Title.Length > 200)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "Title must be between 1 and 200 characters"
                }
            });
        }

        if (request.Copies < 1 || request.Copies > 100)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "Copies must be between 1 and 100"
                }
            });
        }

        var bookId = $"bk_{_bookSequence++:D10}";

        using (var connection = _db.GetConnection())
        using (var transaction = connection.BeginTransaction())
        {
            try
            {
                var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Books (Id, Title, Copies) VALUES (@id, @title, @copies)";
                command.Parameters.AddWithValue("@id", bookId);
                command.Parameters.AddWithValue("@title", request.Title);
                command.Parameters.AddWithValue("@copies", request.Copies);
                command.ExecuteNonQuery();

                transaction.Commit();

                return Results.Created($"/v1/books/{bookId}", new BookResponse
                {
                    Id = bookId,
                    Title = request.Title,
                    Copies = request.Copies,
                    AvailableCopies = request.Copies
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public async Task<IResult> GetBook(string id)
    {
        using var connection = _db.GetConnection();

        // Get book
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, Copies FROM Books WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return Results.NotFound(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "not_found",
                    Message = "Book not found"
                }
            });
        }

        var title = reader.GetString(1);
        var copies = reader.GetInt32(2);
        reader.Close();

        // Count active loans
        var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE BookId = @bookId AND Status = 'active'";
        countCmd.Parameters.AddWithValue("@bookId", id);
        var activeLoanCount = (long)countCmd.ExecuteScalar()!;
        var availableCopies = copies - (int)activeLoanCount;

        return Results.Ok(new BookResponse
        {
            Id = id,
            Title = title,
            Copies = copies,
            AvailableCopies = availableCopies
        });
    }

    public async Task<IResult> CreateMember(CreateMemberRequest request)
    {
        if (string.IsNullOrEmpty(request.Name) || request.Name.Length < 1 || request.Name.Length > 100)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "Name must be between 1 and 100 characters"
                }
            });
        }

        var memberId = $"mb_{_memberSequence++:D10}";

        using (var connection = _db.GetConnection())
        using (var transaction = connection.BeginTransaction())
        {
            try
            {
                var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Members (Id, Name) VALUES (@id, @name)";
                command.Parameters.AddWithValue("@id", memberId);
                command.Parameters.AddWithValue("@name", request.Name);
                command.ExecuteNonQuery();

                transaction.Commit();

                return Results.Created($"/v1/members/{memberId}", new MemberResponse
                {
                    Id = memberId,
                    Name = request.Name
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public async Task<IResult> CreateLoan(CreateLoanRequest request)
    {
        // 1. Input validation (§2.4)
        if (string.IsNullOrEmpty(request.BookId) || string.IsNullOrEmpty(request.MemberId) ||
            string.IsNullOrEmpty(request.LoanedAtUtc))
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "Missing required fields"
                }
            });
        }

        var (isValidDateTime, loanedAtUtc) = ParseStrictDateTime(request.LoanedAtUtc);
        if (!isValidDateTime || loanedAtUtc == null)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "Invalid datetime format"
                }
            });
        }

        using (var connection = _db.GetConnection())
        using (var transaction = connection.BeginTransaction())
        {
            try
            {
                // 2. Check existence
                var bookCmd = connection.CreateCommand();
                bookCmd.CommandText = "SELECT Copies FROM Books WHERE Id = @bookId";
                bookCmd.Parameters.AddWithValue("@bookId", request.BookId);
                var bookResult = bookCmd.ExecuteScalar();

                if (bookResult == null)
                {
                    transaction.Rollback();
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "not_found",
                            Message = "Book not found"
                        }
                    });
                }

                var bookCopies = (int)bookResult;

                var memberCmd = connection.CreateCommand();
                memberCmd.CommandText = "SELECT 1 FROM Members WHERE Id = @memberId";
                memberCmd.Parameters.AddWithValue("@memberId", request.MemberId);
                var memberExists = memberCmd.ExecuteScalar() != null;

                if (!memberExists)
                {
                    transaction.Rollback();
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "not_found",
                            Message = "Member not found"
                        }
                    });
                }

                // Count active loans
                var countCmd = connection.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE BookId = @bookId AND Status = 'active'";
                countCmd.Parameters.AddWithValue("@bookId", request.BookId);
                var activeLoanCountForBook = (long)countCmd.ExecuteScalar()!;

                countCmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE MemberId = @memberId AND Status = 'active'";
                countCmd.Parameters.Clear();
                countCmd.Parameters.AddWithValue("@memberId", request.MemberId);
                var activeLoanCountForMember = (long)countCmd.ExecuteScalar()!;

                // Get member's active loans for overdue check
                var loansCmd = connection.CreateCommand();
                loansCmd.CommandText = "SELECT DueDateUtc FROM Loans WHERE MemberId = @memberId AND Status = 'active'";
                loansCmd.Parameters.AddWithValue("@memberId", request.MemberId);
                var loansReader = loansCmd.ExecuteReader();
                var memberLoans = new List<(DateOnly dueDate, bool isActive)>();
                while (loansReader.Read())
                {
                    var dueDateStr = loansReader.GetString(0);
                    if (DateOnly.TryParse(dueDateStr, out var dueDate))
                    {
                        memberLoans.Add((dueDate, true));
                    }
                }
                loansReader.Close();

                // Validate via domain service
                var validation = _lending.ValidateLoan(
                    request.BookId,
                    request.MemberId,
                    loanedAtUtc.Value,
                    bookCopies,
                    (int)activeLoanCountForBook,
                    (int)activeLoanCountForMember,
                    memberLoans);

                if (!validation.IsValid)
                {
                    transaction.Rollback();
                    var errorResponse = new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = validation.ErrorCode ?? "invalid_request",
                            Message = validation.ErrorMessage ?? "Invalid request"
                        }
                    };
                    var statusCode = validation.ErrorCode switch
                    {
                        "member_overdue_blocked" => StatusCodes.Status409Conflict,
                        "loan_limit_exceeded" => StatusCodes.Status409Conflict,
                        "no_copies_available" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status400BadRequest
                    };
                    return Results.Json(errorResponse, statusCode: statusCode);
                }

                var loanId = $"ln_{_loanSequence++:D10}";
                var dueDateCalc = _lending.CalculateDueDate(loanedAtUtc.Value);
                var dueDateString = dueDateCalc.ToString("yyyy-MM-dd");

                var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Loans (Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status)
                    VALUES (@id, @bookId, @memberId, @loanedAtUtc, @dueDateUtc, 'active')";
                insertCmd.Parameters.AddWithValue("@id", loanId);
                insertCmd.Parameters.AddWithValue("@bookId", request.BookId);
                insertCmd.Parameters.AddWithValue("@memberId", request.MemberId);
                insertCmd.Parameters.AddWithValue("@loanedAtUtc", loanedAtUtc.Value.ToString("O"));
                insertCmd.Parameters.AddWithValue("@dueDateUtc", dueDateString);
                insertCmd.ExecuteNonQuery();

                transaction.Commit();

                return Results.Created($"/v1/loans/{loanId}", new CreateLoanResponse
                {
                    Id = loanId,
                    BookId = request.BookId,
                    MemberId = request.MemberId,
                    LoanedAtUtc = loanedAtUtc.Value.ToString("O"),
                    DueDateUtc = dueDateString,
                    Status = "active"
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public async Task<IResult> ReturnLoan(string id, ReturnLoanRequest request)
    {
        // 1. Input validation
        if (string.IsNullOrEmpty(request.ReturnedAtUtc))
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "Missing returnedAtUtc"
                }
            });
        }

        var (isValidDateTime, returnedAtUtc) = ParseStrictDateTime(request.ReturnedAtUtc);
        if (!isValidDateTime || returnedAtUtc == null)
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "Invalid datetime format"
                }
            });
        }

        using (var connection = _db.GetConnection())
        using (var transaction = connection.BeginTransaction())
        {
            try
            {
                // 2. Check loan exists
                var loanCmd = connection.CreateCommand();
                loanCmd.CommandText = "SELECT LoanedAtUtc, DueDateUtc, Status FROM Loans WHERE Id = @id";
                loanCmd.Parameters.AddWithValue("@id", id);
                var loanReader = loanCmd.ExecuteReader();

                if (!loanReader.Read())
                {
                    transaction.Rollback();
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "not_found",
                            Message = "Loan not found"
                        }
                    });
                }

                var loanedAtUtcStr = loanReader.GetString(0);
                var dueDateStr = loanReader.GetString(1);
                var status = loanReader.GetString(2);
                loanReader.Close();

                if (!DateTime.TryParse(loanedAtUtcStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var loanedAtUtc))
                {
                    transaction.Rollback();
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "invalid_request",
                            Message = "Invalid loan datetime"
                        }
                    });
                }

                if (!DateOnly.TryParse(dueDateStr, out var dueDate))
                {
                    transaction.Rollback();
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = "invalid_request",
                            Message = "Invalid due date"
                        }
                    });
                }

                // 3. & 4. Validate return
                var validation = _lending.ValidateReturn(loanedAtUtc, returnedAtUtc.Value, status);
                if (!validation.IsValid)
                {
                    transaction.Rollback();
                    var errorResponse = new ErrorResponse
                    {
                        Error = new ErrorDetail
                        {
                            Code = validation.ErrorCode ?? "invalid_request",
                            Message = validation.ErrorMessage ?? "Invalid request"
                        }
                    };
                    var statusCode = validation.ErrorCode switch
                    {
                        "already_returned" => StatusCodes.Status409Conflict,
                        "invalid_request" => StatusCodes.Status400BadRequest,
                        _ => StatusCodes.Status400BadRequest
                    };
                    return Results.Json(errorResponse, statusCode: statusCode);
                }

                var fineAmount = _lending.CalculateFine(returnedAtUtc.Value, dueDate);

                var updateCmd = connection.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE Loans
                    SET Status = 'returned', ReturnedAtUtc = @returnedAtUtc, FineAmount = @fineAmount
                    WHERE Id = @id";
                updateCmd.Parameters.AddWithValue("@returnedAtUtc", returnedAtUtc.Value.ToString("O"));
                updateCmd.Parameters.AddWithValue("@fineAmount", fineAmount);
                updateCmd.Parameters.AddWithValue("@id", id);
                updateCmd.ExecuteNonQuery();

                transaction.Commit();

                return Results.Ok(new ReturnLoanResponse
                {
                    Id = id,
                    BookId = GetBookIdForLoan(connection, id),
                    MemberId = GetMemberIdForLoan(connection, id),
                    LoanedAtUtc = loanedAtUtcStr,
                    DueDateUtc = dueDateStr,
                    Status = "returned",
                    ReturnedAtUtc = returnedAtUtc.Value.ToString("O"),
                    FineAmount = fineAmount
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public async Task<IResult> ListLoans(string? memberId)
    {
        // Validate input
        if (string.IsNullOrEmpty(memberId))
        {
            return Results.BadRequest(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "invalid_request",
                    Message = "memberId parameter is required"
                }
            });
        }

        using var connection = _db.GetConnection();

        // Check member exists
        var memberCmd = connection.CreateCommand();
        memberCmd.CommandText = "SELECT 1 FROM Members WHERE Id = @memberId";
        memberCmd.Parameters.AddWithValue("@memberId", memberId);
        if (memberCmd.ExecuteScalar() == null)
        {
            return Results.NotFound(new ErrorResponse
            {
                Error = new ErrorDetail
                {
                    Code = "not_found",
                    Message = "Member not found"
                }
            });
        }

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount FROM Loans WHERE MemberId = @memberId ORDER BY LoanedAtUtc ASC, Id ASC";
        cmd.Parameters.AddWithValue("@memberId", memberId);
        var reader = cmd.ExecuteReader();

        var items = new List<object>();
        while (reader.Read())
        {
            var loanId = reader.GetString(0);
            var bookId = reader.GetString(1);
            var memberIdVal = reader.GetString(2);
            var loanedAtUtc = reader.GetString(3);
            var dueDateUtc = reader.GetString(4);
            var loanStatus = reader.GetString(5);
            var returnedAtUtcObj = reader.GetValue(6);
            var fineAmountObj = reader.GetValue(7);

            if (loanStatus == "active")
            {
                items.Add(new
                {
                    id = loanId,
                    bookId = bookId,
                    memberId = memberIdVal,
                    loanedAtUtc = loanedAtUtc,
                    dueDateUtc = dueDateUtc,
                    status = loanStatus
                });
            }
            else
            {
                items.Add(new
                {
                    id = loanId,
                    bookId = bookId,
                    memberId = memberIdVal,
                    loanedAtUtc = loanedAtUtc,
                    dueDateUtc = dueDateUtc,
                    status = loanStatus,
                    returnedAtUtc = returnedAtUtcObj == DBNull.Value ? null : returnedAtUtcObj.ToString(),
                    fineAmount = fineAmountObj == DBNull.Value ? null : (int?)fineAmountObj
                });
            }
        }
        reader.Close();

        return Results.Ok(new ListLoansResponse { Items = items });
    }

    private string GetBookIdForLoan(SqliteConnection connection, string loanId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT BookId FROM Loans WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", loanId);
        return (string)(cmd.ExecuteScalar() ?? "");
    }

    private string GetMemberIdForLoan(SqliteConnection connection, string loanId)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MemberId FROM Loans WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", loanId);
        return (string)(cmd.ExecuteScalar() ?? "");
    }
}
