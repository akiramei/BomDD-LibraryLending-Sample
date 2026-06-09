using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Library.Api;

// ── DTO records returned from DB queries ──────────────────────────────────

public record BookRow(string Id, string Title, int Copies);
public record MemberRow(string Id, string Name);
public record LoanRow(
    string Id,
    string BookId,
    string MemberId,
    string LoanedAtUtc,   // stored as yyyy-MM-ddTHH:mm:ssZ
    string DueDateUtc,    // stored as yyyy-MM-dd
    string Status,        // "active" | "returned"
    string? ReturnedAtUtc,
    int? FineAmount);

/// <summary>
/// All SQLite operations for the library API (K-SQLITE-001).
/// Each public method opens its own connection (request-scoped).
/// Write operations use a single transaction for atomicity (INV-1/INV-2).
/// </summary>
public class Database
{
    private readonly string _connectionString;

    public Database(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // ── Schema bootstrap ─────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Books (
                Id      TEXT PRIMARY KEY,
                Title   TEXT NOT NULL,
                Copies  INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Members (
                Id   TEXT PRIMARY KEY,
                Name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Loans (
                Id             TEXT PRIMARY KEY,
                BookId         TEXT NOT NULL,
                MemberId       TEXT NOT NULL,
                LoanedAtUtc    TEXT NOT NULL,
                DueDateUtc     TEXT NOT NULL,
                Status         TEXT NOT NULL DEFAULT 'active',
                ReturnedAtUtc  TEXT,
                FineAmount     INTEGER,
                FOREIGN KEY (BookId)   REFERENCES Books(Id),
                FOREIGN KEY (MemberId) REFERENCES Members(Id)
            );";
        cmd.ExecuteNonQuery();
    }

    // ── Books ─────────────────────────────────────────────────────────────

    public void InsertBook(string id, string title, int copies)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Books (Id, Title, Copies) VALUES ($id, $title, $copies)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$copies", copies);
        cmd.ExecuteNonQuery();
    }

    public BookRow? GetBook(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Copies FROM Books WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new BookRow(r.GetString(0), r.GetString(1), r.GetInt32(2));
    }

    public int GetActiveLoansForBook(string bookId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE BookId = $bookId AND Status = 'active'";
        cmd.Parameters.AddWithValue("$bookId", bookId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── Members ──────────────────────────────────────────────────────────

    public void InsertMember(string id, string name)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Members (Id, Name) VALUES ($id, $name)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    public MemberRow? GetMember(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name FROM Members WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new MemberRow(r.GetString(0), r.GetString(1));
    }

    // ── Loans ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Atomically checks constraints and inserts a loan (K-SQLITE-001 transaction).
    /// Returns error code string or null on success.
    /// </summary>
    public (string? errorCode, LoanRow? loan) TryInsertLoan(
        string loanId,
        string bookId,
        string memberId,
        string loanedAtUtc,
        string dueDateUtc,
        System.DateTimeOffset loanedAt)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        // --- Step 3: overdue check (any active loan of member overdue?) ---
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT DueDateUtc FROM Loans WHERE MemberId = $mid AND Status = 'active'";
            cmd.Parameters.AddWithValue("$mid", memberId);
            using var r = cmd.ExecuteReader();
            var loanedDay = System.DateOnly.FromDateTime(loanedAt.UtcDateTime);
            while (r.Read())
            {
                var due = System.DateOnly.Parse(r.GetString(0));
                if (loanedDay > due)
                {
                    tx.Rollback();
                    return ("member_overdue_blocked", null);
                }
            }
        }

        // --- Step 4: loan limit (active count) ---
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE MemberId = $mid AND Status = 'active'";
            cmd.Parameters.AddWithValue("$mid", memberId);
            int count = Convert.ToInt32(cmd.ExecuteScalar());
            if (count >= 3)
            {
                tx.Rollback();
                return ("loan_limit_exceeded", null);
            }
        }

        // --- Step 5: available copies ---
        int copies, activeForBook;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT Copies FROM Books WHERE Id = $bid";
            cmd.Parameters.AddWithValue("$bid", bookId);
            copies = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE BookId = $bid AND Status = 'active'";
            cmd.Parameters.AddWithValue("$bid", bookId);
            activeForBook = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (copies - activeForBook <= 0)
        {
            tx.Rollback();
            return ("no_copies_available", null);
        }

        // --- Insert ---
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Loans (Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status)
                                 VALUES ($id, $bid, $mid, $loa, $due, 'active')";
            cmd.Parameters.AddWithValue("$id", loanId);
            cmd.Parameters.AddWithValue("$bid", bookId);
            cmd.Parameters.AddWithValue("$mid", memberId);
            cmd.Parameters.AddWithValue("$loa", loanedAtUtc);
            cmd.Parameters.AddWithValue("$due", dueDateUtc);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        return (null, new LoanRow(loanId, bookId, memberId, loanedAtUtc, dueDateUtc, "active", null, null));
    }

    public LoanRow? GetLoan(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount
                            FROM Loans WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadLoanRow(r);
    }

    /// <summary>
    /// Atomically marks a loan as returned (K-SQLITE-001 transaction).
    /// Returns error code or null on success.
    /// </summary>
    public (string? errorCode, LoanRow? loan) TryReturnLoan(
        string loanId,
        string returnedAtUtc,
        int fineAmount)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        LoanRow? existing;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount
                                FROM Loans WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", loanId);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                tx.Rollback();
                return ("not_found", null);
            }
            existing = ReadLoanRow(r);
        }

        if (existing.Status == "returned")
        {
            tx.Rollback();
            return ("already_returned", null);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE Loans SET Status='returned', ReturnedAtUtc=$ret, FineAmount=$fine
                                WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", loanId);
            cmd.Parameters.AddWithValue("$ret", returnedAtUtc);
            cmd.Parameters.AddWithValue("$fine", fineAmount);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        return (null, existing with { Status = "returned", ReturnedAtUtc = returnedAtUtc, FineAmount = fineAmount });
    }

    public List<LoanRow> GetLoansForMember(string memberId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // Order by LoanedAtUtc (stored as yyyy-MM-ddTHH:mm:ssZ which sorts lexicographically = chronologically), then Id ordinal
        cmd.CommandText = @"SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount
                            FROM Loans WHERE MemberId = $mid
                            ORDER BY LoanedAtUtc ASC, Id ASC";
        cmd.Parameters.AddWithValue("$mid", memberId);
        using var r = cmd.ExecuteReader();
        var list = new List<LoanRow>();
        while (r.Read())
            list.Add(ReadLoanRow(r));
        return list;
    }

    private static LoanRow ReadLoanRow(SqliteDataReader r)
    {
        return new LoanRow(
            r.GetString(0),
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetInt32(7));
    }
}
