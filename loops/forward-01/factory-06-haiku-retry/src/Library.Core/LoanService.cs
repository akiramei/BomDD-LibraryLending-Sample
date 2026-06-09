using System;

namespace Library.Core;

public record Book(string Id, string Title, int Copies, int AvailableCopies);
public record Member(string Id, string Name);
public record LoanDto(string Id, string BookId, string MemberId, string LoanedAtUtc, string DueDateUtc, string Status, string? ReturnedAtUtc = null, int? FineAmount = null);

public interface ILoanRepository
{
    void SaveBook(Book book);
    Book? GetBook(string id);
    void SaveMember(Member member);
    Member? GetMember(string id);
    void SaveLoan(LoanDto loan);
    LoanDto? GetLoan(string id);
    List<LoanDto> GetLoansByMember(string memberId);
    int GetActiveLoanCountForBook(string bookId);
    int GetActiveLoanCountForMember(string memberId);
}

public class LoanService
{
    private readonly ILoanRepository _repo;

    public LoanService(ILoanRepository repo)
    {
        _repo = repo;
    }

    public string GenerateId(string prefix)
    {
        // ID = prefix + 32 digits of lowercase hex (GUID "N" format)
        return prefix + Guid.NewGuid().ToString("N");
    }

    public Book RegisterBook(string title, int copies)
    {
        var book = new Book(
            Id: GenerateId("bk_"),
            Title: title,
            Copies: copies,
            AvailableCopies: copies
        );
        _repo.SaveBook(book);
        return book;
    }

    public Member RegisterMember(string name)
    {
        var member = new Member(
            Id: GenerateId("mb_"),
            Name: name
        );
        _repo.SaveMember(member);
        return member;
    }

    /// <summary>
    /// Parse ISO-8601 datetime with strict literal-Z validation.
    /// Accepts: yyyy-MM-ddTHH:mm:ssZ or with 1-7 fractional seconds (yyyy-MM-ddTHH:mm:ss.fZ)
    /// Rejects: lowercase z, numeric offset (+09:00, +00:00), no offset, date-only
    /// Returns: Parsed DateTime in UTC, with fractional seconds truncated
    /// </summary>
    public static DateTime ParseStrictUtcDateTime(string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new InvalidOperationException("Invalid datetime format");

        // Must end with uppercase Z
        if (!input.EndsWith("Z"))
            throw new InvalidOperationException("Invalid datetime format");

        // Check for forbidden patterns (lowercase z, numeric offsets with +/-)
        if (input.Contains('+') || input.Contains('z'))
            throw new InvalidOperationException("Invalid datetime format");

        // Check for timezone offset like +00:00 (count hyphens - if more than 2, it's likely an offset)
        int dashCount = input.Count(c => c == '-');
        if (dashCount > 2)  // yyyy-MM-dd has 2 dashes; more means there's a timezone offset
            throw new InvalidOperationException("Invalid datetime format");

        string withoutZ = input[..^1];

        // Try to parse: yyyy-MM-ddTHH:mm:ss or with fractional seconds
        if (!DateTime.TryParseExact(
            withoutZ,
            new[] { "yyyy-MM-ddTHH:mm:ss.fffffff", "yyyy-MM-ddTHH:mm:ss.ffffff", "yyyy-MM-ddTHH:mm:ss.fffff",
                    "yyyy-MM-ddTHH:mm:ss.ffff", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.ff", "yyyy-MM-ddTHH:mm:ss.f", "yyyy-MM-ddTHH:mm:ss" },
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt))
        {
            throw new InvalidOperationException("Invalid datetime format");
        }

        // Ensure UTC kind
        if (dt.Kind != DateTimeKind.Utc)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        // Truncate to seconds (remove fractional seconds)
        return dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));
    }

    public static string FormatUtcDateTime(DateTime dt)
    {
        // Output: yyyy-MM-ddTHH:mm:ssZ (seconds precision, no fractional seconds)
        return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    public static string FormatDueDate(DateTime dt)
    {
        // DueDateUtc: yyyy-MM-dd (10 characters, date only)
        return dt.ToUniversalTime().ToString("yyyy-MM-dd");
    }

    public static DateOnly GetUtcDate(DateTime dt)
    {
        return DateOnly.FromDateTime(dt.ToUniversalTime());
    }

    public static DateOnly AddDays(DateOnly date, int days)
    {
        return date.AddDays(days);
    }

    /// <summary>
    /// Loan a book to a member.
    /// Validation order (return first error):
    /// 1. Input validation (missing/invalid fields/datetime format)
    /// 2. BookId/MemberId existence
    /// 3. Member overdue check (any active loan past due)
    /// 4. Member loan limit (max 3 active)
    /// 5. Book availability (availableCopies > 0)
    /// </summary>
    public (LoanDto? Loan, string? ErrorCode) CreateLoan(string bookId, string memberId, string loanedAtUtcStr)
    {
        // Validation 1: Parse datetime (strict literal-Z)
        DateTime loanedAt;
        try
        {
            loanedAt = ParseStrictUtcDateTime(loanedAtUtcStr);
        }
        catch
        {
            return (null, "invalid_request");
        }

        // Validation 1: Check non-empty strings
        if (string.IsNullOrWhiteSpace(bookId) || string.IsNullOrWhiteSpace(memberId))
            return (null, "invalid_request");

        // Validation 2: Check existence
        var book = _repo.GetBook(bookId);
        var member = _repo.GetMember(memberId);
        if (book == null || member == null)
            return (null, "not_found");

        // Validation 3: Check overdue
        var memberLoans = _repo.GetLoansByMember(memberId);
        var loanedDate = GetUtcDate(loanedAt);

        foreach (var loan in memberLoans)
        {
            if (loan.Status == "active")
            {
                var existingDueDateStr = loan.DueDateUtc;
                var existingDueDate = DateOnly.ParseExact(existingDueDateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                // Overdue if loanedDate > dueDate
                if (loanedDate > existingDueDate)
                    return (null, "member_overdue_blocked");
            }
        }

        // Validation 4: Check loan limit
        int activeLoanCount = _repo.GetActiveLoanCountForMember(memberId);
        if (activeLoanCount >= 3)
            return (null, "loan_limit_exceeded");

        // Validation 5: Check availability
        int activeBookLoans = _repo.GetActiveLoanCountForBook(bookId);
        if (activeBookLoans >= book.Copies)
            return (null, "no_copies_available");

        // Create loan
        var loanId = GenerateId("ln_");
        // Due date = loan date (ignoring time) + 14 calendar days
        var loanDt = loanedAt.ToUniversalTime();
        var dueDateTime = new DateTime(loanDt.Year, loanDt.Month, loanDt.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(14);
        var newDueDateStr = dueDateTime.ToString("yyyy-MM-dd");

        var loanDto = new LoanDto(
            Id: loanId,
            BookId: bookId,
            MemberId: memberId,
            LoanedAtUtc: FormatUtcDateTime(loanedAt),
            DueDateUtc: newDueDateStr,
            Status: "active"
        );

        _repo.SaveLoan(loanDto);
        return (loanDto, null);
    }

    /// <summary>
    /// Return a loaned book.
    /// Validation order:
    /// 1. Input validation (missing/invalid datetime format)
    /// 2. Loan existence
    /// 3. Loan already returned
    /// 4. returnedAtUtc >= loanedAtUtc (moment comparison, not date)
    /// </summary>
    public (LoanDto? Loan, string? ErrorCode) ReturnLoan(string loanId, string returnedAtUtcStr)
    {
        // Validation 1: Parse datetime
        DateTime returnedAt;
        try
        {
            returnedAt = ParseStrictUtcDateTime(returnedAtUtcStr);
        }
        catch
        {
            return (null, "invalid_request");
        }

        // Validation 2: Check loan exists
        var loan = _repo.GetLoan(loanId);
        if (loan == null)
            return (null, "not_found");

        // Validation 3: Check not already returned
        if (loan.Status == "returned")
            return (null, "already_returned");

        // Validation 4: Check returnedAt >= loanedAt (moment comparison)
        var loanedAt = ParseStrictUtcDateTime(loan.LoanedAtUtc);
        if (returnedAt < loanedAt)
            return (null, "invalid_request");

        // Calculate fine
        var loanedDate = GetUtcDate(loanedAt);
        var returnedDate = GetUtcDate(returnedAt);
        var dueDate = DateOnly.ParseExact(loan.DueDateUtc, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // Days beyond due: if returnedDate > dueDate, count the days after
        // Create temporary DateTimes for calculation
        var dueDateAsDateTime = new DateTime(dueDate.Year, dueDate.Month, dueDate.Day, 0, 0, 0, DateTimeKind.Utc);
        var returnedDateAsDateTime = new DateTime(returnedDate.Year, returnedDate.Month, returnedDate.Day, 0, 0, 0, DateTimeKind.Utc);

        int daysBeyondDue = (int)(returnedDateAsDateTime - dueDateAsDateTime).TotalDays;
        int fineAmount = Math.Max(0, daysBeyondDue) * 100;

        var returnedLoan = loan with
        {
            Status = "returned",
            ReturnedAtUtc = FormatUtcDateTime(returnedAt),
            FineAmount = fineAmount
        };

        _repo.SaveLoan(returnedLoan);
        return (returnedLoan, null);
    }

    public Book? GetBook(string id)
    {
        return _repo.GetBook(id);
    }

    public List<LoanDto> GetLoansByMember(string memberId)
    {
        var loans = _repo.GetLoansByMember(memberId);

        // Sort: loanedAtUtc ascending (by moment), then id ordinal ascending
        loans.Sort((a, b) =>
        {
            var aTime = ParseStrictUtcDateTime(a.LoanedAtUtc);
            var bTime = ParseStrictUtcDateTime(b.LoanedAtUtc);
            var cmp = aTime.CompareTo(bTime);
            if (cmp != 0) return cmp;
            return StringComparer.Ordinal.Compare(a.Id, b.Id);
        });

        return loans;
    }
}
