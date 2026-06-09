using Microsoft.Data.Sqlite;
using Library.Core;

namespace Library.Api;

/// <summary>
/// SQLite persistence layer (K-SQLITE-001, E-SQLITE-STORE-001).
/// - One connection per call (pooling via Microsoft.Data.Sqlite default).
/// - Write ops use a single transaction for atomicity (INV-1, INV-2).
/// - Schema: CREATE TABLE IF NOT EXISTS at startup.
/// CHEAT-F01-004: chose TEXT storage for DateTime (ISO-8601 Z string), DateOnly (yyyy-MM-dd).
/// CHEAT-F01-005: no WAL pragma (default journal_mode, sufficient for single-writer API).
/// </summary>
public class Database
{
    private readonly string _connectionString;

    public Database(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Books (
                Id      TEXT NOT NULL PRIMARY KEY,
                Title   TEXT NOT NULL,
                Copies  INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Members (
                Id   TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Loans (
                Id             TEXT NOT NULL PRIMARY KEY,
                BookId         TEXT NOT NULL,
                MemberId       TEXT NOT NULL,
                LoanedAtUtc    TEXT NOT NULL,
                DueDateUtc     TEXT NOT NULL,
                Status         TEXT NOT NULL DEFAULT 'active',
                ReturnedAtUtc  TEXT,
                FineAmount     INTEGER
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Books ────────────────────────────────────────────────────

    public Book? GetBook(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Copies FROM Books WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Book(reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
    }

    public Book InsertBook(string id, string title, int copies)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Books (Id, Title, Copies) VALUES (@id, @title, @copies)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@copies", copies);
        cmd.ExecuteNonQuery();
        return new Book(id, title, copies);
    }

    public int GetActiveLoansCount(string bookId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE BookId = @bookId AND Status = 'active'";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── Members ──────────────────────────────────────────────────

    public Member? GetMember(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name FROM Members WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Member(reader.GetString(0), reader.GetString(1));
    }

    public Member InsertMember(string id, string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Members (Id, Name) VALUES (@id, @name)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
        return new Member(id, name);
    }

    // ── Loans ────────────────────────────────────────────────────

    public Loan? GetLoan(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount FROM Loans WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadLoan(reader);
    }

    public IReadOnlyList<Loan> GetActiveLoansForMember(string memberId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount FROM Loans WHERE MemberId = @memberId AND Status = 'active'";
        cmd.Parameters.AddWithValue("@memberId", memberId);
        using var reader = cmd.ExecuteReader();
        var list = new List<Loan>();
        while (reader.Read()) list.Add(ReadLoan(reader));
        return list;
    }

    public IReadOnlyList<Loan> GetLoansForMember(string memberId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount FROM Loans WHERE MemberId = @memberId";
        cmd.Parameters.AddWithValue("@memberId", memberId);
        using var reader = cmd.ExecuteReader();
        var list = new List<Loan>();
        while (reader.Read()) list.Add(ReadLoan(reader));
        return list;
    }

    /// <summary>
    /// Atomically checks eligibility and inserts a new loan.
    /// Returns (loan, null) on success or (null, error) on failure.
    /// </summary>
    public (Loan? loan, DomainError? error) TryCreateLoan(
        string bookId, string memberId, DateTime loanedAtUtc)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        // Fetch book
        Book? book;
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, Title, Copies FROM Books WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", bookId);
            using var r = cmd.ExecuteReader();
            book = r.Read() ? new Book(r.GetString(0), r.GetString(1), r.GetInt32(2)) : null;
        }

        // Fetch member
        Member? member;
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, Name FROM Members WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", memberId);
            using var r = cmd.ExecuteReader();
            member = r.Read() ? new Member(r.GetString(0), r.GetString(1)) : null;
        }

        // Fetch active loans for member
        IReadOnlyList<Loan> memberActiveLoans;
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount FROM Loans WHERE MemberId = @memberId AND Status = 'active'";
            cmd.Parameters.AddWithValue("@memberId", memberId);
            using var r = cmd.ExecuteReader();
            var list = new List<Loan>();
            while (r.Read()) list.Add(ReadLoan(r));
            memberActiveLoans = list;
        }

        // Count active loans for book
        int activeBookLoans;
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE BookId = @bookId AND Status = 'active'";
            cmd.Parameters.AddWithValue("@bookId", bookId);
            activeBookLoans = Convert.ToInt32(cmd.ExecuteScalar());
        }

        int availableCopies = book == null ? 0 : book.Copies - activeBookLoans;

        var error = LendingDomain.CheckLoanEligibility(book, member, memberActiveLoans, availableCopies, loanedAtUtc);
        if (error != null)
        {
            tx.Rollback();
            return (null, error);
        }

        // Insert loan
        var loanId = IdGenerator.NewLoanId();
        var dueDate = LendingDomain.CalculateDueDate(loanedAtUtc);
        var loanedAtStr = DateTimeParser.Format(loanedAtUtc);
        var dueDateStr = dueDate.ToString("yyyy-MM-dd");

        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Loans (Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status)
                VALUES (@id, @bookId, @memberId, @loanedAt, @dueDate, 'active')
                """;
            cmd.Parameters.AddWithValue("@id", loanId);
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@memberId", memberId);
            cmd.Parameters.AddWithValue("@loanedAt", loanedAtStr);
            cmd.Parameters.AddWithValue("@dueDate", dueDateStr);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();

        var loan = new Loan(loanId, bookId, memberId, loanedAtUtc, dueDate, LoanStatus.Active);
        return (loan, null);
    }

    /// <summary>
    /// Atomically checks return eligibility and updates loan status.
    /// </summary>
    public (Loan? loan, DomainError? error) TryReturnLoan(string loanId, DateTime returnedAtUtc)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        Loan? loan;
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount FROM Loans WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", loanId);
            using var r = cmd.ExecuteReader();
            loan = r.Read() ? ReadLoan(r) : null;
        }

        if (loan == null)
        {
            tx.Rollback();
            return (null, new DomainError.NotFound("Loan not found."));
        }

        var error = LendingDomain.CheckReturnEligibility(loan, returnedAtUtc);
        if (error != null)
        {
            tx.Rollback();
            return (null, error);
        }

        var fine = LendingDomain.CalculateFine(returnedAtUtc, loan.DueDateUtc);
        var returnedAtStr = DateTimeParser.Format(returnedAtUtc);

        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE Loans SET Status = 'returned', ReturnedAtUtc = @returnedAt, FineAmount = @fine
                WHERE Id = @id
                """;
            cmd.Parameters.AddWithValue("@returnedAt", returnedAtStr);
            cmd.Parameters.AddWithValue("@fine", fine);
            cmd.Parameters.AddWithValue("@id", loanId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();

        var updated = loan with { Status = LoanStatus.Returned, ReturnedAtUtc = returnedAtUtc, FineAmount = fine };
        return (updated, null);
    }

    private static Loan ReadLoan(SqliteDataReader r)
    {
        var status = r.GetString(5) == "returned" ? LoanStatus.Returned : LoanStatus.Active;
        DateTime? returnedAt = r.IsDBNull(6) ? null : ParseStoredDateTime(r.GetString(6));
        int? fine = r.IsDBNull(7) ? null : r.GetInt32(7);

        return new Loan(
            Id:            r.GetString(0),
            BookId:        r.GetString(1),
            MemberId:      r.GetString(2),
            LoanedAtUtc:   ParseStoredDateTime(r.GetString(3)),
            DueDateUtc:    DateOnly.ParseExact(r.GetString(4), "yyyy-MM-dd"),
            Status:        status,
            ReturnedAtUtc: returnedAt,
            FineAmount:    fine
        );
    }

    private static DateTime ParseStoredDateTime(string s)
    {
        // Stored as yyyy-MM-ddTHH:mm:ssZ  (normalized by DateTimeParser.Format)
        return DateTime.ParseExact(s, "yyyy-MM-ddTHH:mm:ssZ",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
    }
}
