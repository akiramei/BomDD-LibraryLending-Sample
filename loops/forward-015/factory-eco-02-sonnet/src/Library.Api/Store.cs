using Microsoft.Data.Sqlite;

namespace Library.Api;

/// <summary>
/// SQLite persistence (K-SQLITE-001). Single file at LIBRARY_DB_PATH (default ./library.db).
/// Connection opened/closed per operation. Writes use a single transaction (INV-1/INV-2).
/// Internal schema is implementation-private (spec §2.7) and verified only via API behavior.
/// </summary>
public sealed class Store
{
    private readonly string _connectionString;

    public Store(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString();
    }

    public static string ResolveDbPath()
        => Environment.GetEnvironmentVariable("LIBRARY_DB_PATH") is { Length: > 0 } p
            ? p
            : "./library.db";

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Ensure schema is at rev3 (members.member_type column exists).
    /// If the DB was created by v0.2 (no member_type column), migrate automatically (REQ-009 / §2.7 rev3):
    ///   1. Add member_type column with default 'standard' for existing rows (ALTER TABLE ... ADD COLUMN).
    ///   2. Update user_version PRAGMA to 3.
    /// Migration is irreversible and uses ALTER/INSERT only — no DROP (K-SQLITE-001).
    /// Self-verification approach: read PRAGMA user_version after migration and confirm == 3.
    /// </summary>
    public void EnsureSchema()
    {
        using var conn = Open();

        // Read current schema version.
        int schemaVersion;
        using (var vc = conn.CreateCommand())
        {
            vc.CommandText = "PRAGMA user_version";
            schemaVersion = Convert.ToInt32(vc.ExecuteScalar());
        }

        if (schemaVersion == 0)
        {
            // Detect whether this is a fresh DB (no tables) or a v0.2 DB (tables exist, no member_type).
            bool membersTableExists;
            using (var tc = conn.CreateCommand())
            {
                tc.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='members'";
                membersTableExists = Convert.ToInt32(tc.ExecuteScalar()) > 0;
            }

            if (membersTableExists)
            {
                // v0.2 DB detected: migrate by adding member_type column.
                bool memberTypeColumnExists;
                using (var cc = conn.CreateCommand())
                {
                    cc.CommandText = "SELECT COUNT(*) FROM pragma_table_info('members') WHERE name='member_type'";
                    memberTypeColumnExists = Convert.ToInt32(cc.ExecuteScalar()) > 0;
                }

                if (!memberTypeColumnExists)
                {
                    // ALTER TABLE to add member_type column; existing rows get 'standard' (REQ-009).
                    using var mc = conn.CreateCommand();
                    mc.CommandText = "ALTER TABLE members ADD COLUMN member_type TEXT NOT NULL DEFAULT 'standard'";
                    mc.ExecuteNonQuery();
                }

                // Stamp version 3.
                using var stamp = conn.CreateCommand();
                stamp.CommandText = "PRAGMA user_version = 3";
                stamp.ExecuteNonQuery();

                schemaVersion = 3;
            }
        }

        // Create tables if not present (fresh DB) and stamp version 3.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS books (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                copies INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS members (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                member_type TEXT NOT NULL DEFAULT 'standard'
            );
            CREATE TABLE IF NOT EXISTS loans (
                id TEXT PRIMARY KEY,
                book_id TEXT NOT NULL,
                member_id TEXT NOT NULL,
                loaned_at_utc TEXT NOT NULL,   -- ISO yyyy-MM-ddTHH:mm:ssZ (second precision)
                due_date_utc TEXT NOT NULL,     -- yyyy-MM-dd
                returned_at_utc TEXT,           -- NULL while active
                fine_amount INTEGER,            -- NULL while active
                status TEXT NOT NULL             -- 'active' | 'returned'
            );
            """;
        cmd.ExecuteNonQuery();

        if (schemaVersion < 3)
        {
            using var stamp2 = conn.CreateCommand();
            stamp2.CommandText = "PRAGMA user_version = 3";
            stamp2.ExecuteNonQuery();
        }
    }

    // ---- Books -------------------------------------------------------------

    public void InsertBook(string id, string title, int copies)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO books (id, title, copies) VALUES ($id, $title, $copies)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$copies", copies);
        cmd.ExecuteNonQuery();
    }

    public BookRow? GetBook(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, copies FROM books WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new BookRow(r.GetString(0), r.GetString(1), r.GetInt32(2));
    }

    /// <summary>availableCopies = copies - active loan count.</summary>
    public int? GetBookAvailableCopies(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT b.copies - (
                SELECT COUNT(*) FROM loans l
                WHERE l.book_id = b.id AND l.status = 'active'
            )
            FROM books b WHERE b.id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    // ---- Members -----------------------------------------------------------

    public void InsertMember(string id, string name, string memberType)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO members (id, name, member_type) VALUES ($id, $name, $memberType)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$memberType", memberType);
        cmd.ExecuteNonQuery();
    }

    public bool MemberExists(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM members WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>Returns (exists, memberType). memberType is "standard" when member not found.</summary>
    public (bool exists, string memberType) GetMemberInfo(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT member_type FROM members WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var result = cmd.ExecuteScalar();
        if (result is null or DBNull) return (false, "standard");
        return (true, result.ToString()!);
    }

    // ---- Loans -------------------------------------------------------------

    public LoanRow? GetLoan(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = LoanSelect + " WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadLoan(r) : null;
    }

    public List<LoanRow> GetLoansByMember(string memberId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = LoanSelect + " WHERE member_id = $mid";
        cmd.Parameters.AddWithValue("$mid", memberId);
        using var r = cmd.ExecuteReader();
        var list = new List<LoanRow>();
        while (r.Read()) list.Add(ReadLoan(r));
        return list;
    }

    private const string LoanSelect =
        "SELECT id, book_id, member_id, loaned_at_utc, due_date_utc, returned_at_utc, fine_amount, status FROM loans";

    private static LoanRow ReadLoan(SqliteDataReader r) => new(
        r.GetString(0),
        r.GetString(1),
        r.GetString(2),
        r.GetString(3),
        r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
        r.GetString(7));

    /// <summary>
    /// Atomically: snapshot decision inputs (book existence/available, member existence,
    /// member active due dates), run the supplied decision, and insert the loan if allowed.
    /// Single transaction so concurrent requests cannot exceed copies/limit (INV-1/INV-2).
    /// </summary>
    public LoanWriteResult CreateLoanAtomic(
        string newLoanId,
        string bookId,
        string memberId,
        DateTimeOffset loanedAtUtc,
        string loanedAtUtcText,
        Func<Library.Core.LoanContext, (Library.Core.LoanDecision decision, string dueDateText)> decide)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        bool bookExists;
        int availableCopies = 0;
        using (var bcmd = conn.CreateCommand())
        {
            bcmd.Transaction = tx;
            bcmd.CommandText = """
                SELECT b.copies - (
                    SELECT COUNT(*) FROM loans l WHERE l.book_id = b.id AND l.status = 'active'
                )
                FROM books b WHERE b.id = $id
                """;
            bcmd.Parameters.AddWithValue("$id", bookId);
            var res = bcmd.ExecuteScalar();
            bookExists = res is not null and not DBNull;
            if (bookExists) availableCopies = Convert.ToInt32(res);
        }

        bool memberExists;
        string memberType = "standard";
        using (var mcmd = conn.CreateCommand())
        {
            mcmd.Transaction = tx;
            mcmd.CommandText = "SELECT member_type FROM members WHERE id = $id";
            mcmd.Parameters.AddWithValue("$id", memberId);
            var mres = mcmd.ExecuteScalar();
            memberExists = mres is not null and not DBNull;
            if (memberExists) memberType = mres!.ToString()!;
        }

        var activeDueDates = new List<DateOnly>();
        using (var lcmd = conn.CreateCommand())
        {
            lcmd.Transaction = tx;
            lcmd.CommandText = "SELECT due_date_utc FROM loans WHERE member_id = $mid AND status = 'active'";
            lcmd.Parameters.AddWithValue("$mid", memberId);
            using var lr = lcmd.ExecuteReader();
            while (lr.Read())
                activeDueDates.Add(DateOnly.ParseExact(lr.GetString(0), "yyyy-MM-dd"));
        }

        var ctx = new Library.Core.LoanContext(
            bookExists, memberExists, loanedAtUtc, activeDueDates, availableCopies, memberType);
        var (decision, dueDateText) = decide(ctx);

        if (decision != Library.Core.LoanDecision.Allowed)
        {
            tx.Rollback();
            return new LoanWriteResult(decision, null);
        }

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO loans (id, book_id, member_id, loaned_at_utc, due_date_utc, returned_at_utc, fine_amount, status)
                VALUES ($id, $bid, $mid, $loaned, $due, NULL, NULL, 'active')
                """;
            ins.Parameters.AddWithValue("$id", newLoanId);
            ins.Parameters.AddWithValue("$bid", bookId);
            ins.Parameters.AddWithValue("$mid", memberId);
            ins.Parameters.AddWithValue("$loaned", loanedAtUtcText);
            ins.Parameters.AddWithValue("$due", dueDateText);
            ins.ExecuteNonQuery();
        }

        tx.Commit();
        var row = new LoanRow(newLoanId, bookId, memberId, loanedAtUtcText, dueDateText, null, null, "active");
        return new LoanWriteResult(Library.Core.LoanDecision.Allowed, row);
    }

    /// <summary>
    /// Atomically re-read the loan, run the return decision, and mark returned if allowed.
    /// Single transaction so a concurrent re-send cannot double-return (INV-4).
    /// </summary>
    public ReturnWriteResult ReturnLoanAtomic(
        string loanId,
        DateTimeOffset returnedAtUtc,
        string returnedAtUtcText,
        Func<LoanRow, (Library.Core.ReturnDecision decision, int fine)> decide)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        LoanRow? loan;
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = LoanSelect + " WHERE id = $id";
            sel.Parameters.AddWithValue("$id", loanId);
            using var r = sel.ExecuteReader();
            loan = r.Read() ? ReadLoan(r) : null;
        }

        if (loan is null)
        {
            tx.Rollback();
            return new ReturnWriteResult(Library.Core.ReturnDecision.NotFound, null);
        }

        var (decision, fine) = decide(loan);
        if (decision != Library.Core.ReturnDecision.Allowed)
        {
            tx.Rollback();
            return new ReturnWriteResult(decision, null);
        }

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE loans SET returned_at_utc = $ret, fine_amount = $fine, status = 'returned'
                WHERE id = $id
                """;
            upd.Parameters.AddWithValue("$ret", returnedAtUtcText);
            upd.Parameters.AddWithValue("$fine", fine);
            upd.Parameters.AddWithValue("$id", loanId);
            upd.ExecuteNonQuery();
        }

        tx.Commit();
        var updated = loan with { ReturnedAtUtc = returnedAtUtcText, FineAmount = fine, Status = "returned" };
        return new ReturnWriteResult(Library.Core.ReturnDecision.Allowed, updated);
    }
}

public sealed record BookRow(string Id, string Title, int Copies);

public sealed record LoanRow(
    string Id,
    string BookId,
    string MemberId,
    string LoanedAtUtc,
    string DueDateUtc,
    string? ReturnedAtUtc,
    int? FineAmount,
    string Status);

public sealed record LoanWriteResult(Library.Core.LoanDecision Decision, LoanRow? Row);
public sealed record ReturnWriteResult(Library.Core.ReturnDecision Decision, LoanRow? Row);
