using Microsoft.Data.Sqlite;
using Library.Core;

namespace Library.Api;

/// <summary>
/// SQLite 永続化ストア(K-SQLITE-001)。
/// - 単一ファイル。パスは LIBRARY_DB_PATH(未設定時 ./library.db)。接続文字列は Data Source=path。
/// - 起動時に CREATE TABLE IF NOT EXISTS。内部スキーマは自由。
/// - 接続はリクエスト/操作ごとに開いて閉じる。グローバル共有接続は持たない。
/// - 書き込み(貸出・返却・登録)は判定と更新を1トランザクション内で行う(INV-1/INV-2 の原子性)。
///
/// 日時は ISO-8601 literal-Z 文字列で保存(瞬時の往復に十分。ソートは文字列でなく読み出し後に DateTimeOffset で行う)。
/// dueDate は yyyy-MM-dd 文字列で保存。
/// </summary>
public sealed class LibraryStore
{
    private readonly string _connectionString;

    public LibraryStore(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString();
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
            CREATE TABLE IF NOT EXISTS books (
                id     TEXT PRIMARY KEY,
                title  TEXT NOT NULL,
                copies INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS members (
                id   TEXT PRIMARY KEY,
                name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS loans (
                id             TEXT PRIMARY KEY,
                book_id        TEXT NOT NULL,
                member_id      TEXT NOT NULL,
                loaned_at_utc  TEXT NOT NULL,
                due_date_utc   TEXT NOT NULL,
                status         TEXT NOT NULL,
                returned_at_utc TEXT NULL,
                fine_amount    INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_loans_member ON loans(member_id);
            CREATE INDEX IF NOT EXISTS ix_loans_book   ON loans(book_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // ---- Books -------------------------------------------------------------

    public Book InsertBook(string title, int copies)
    {
        var id = Ids.NewBookId();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO books (id, title, copies) VALUES ($id, $title, $copies)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$copies", copies);
        cmd.ExecuteNonQuery();
        return new Book(id, title, copies);
    }

    public Book? GetBook(string id)
    {
        using var conn = Open();
        return GetBook(conn, null, id);
    }

    private static Book? GetBook(SqliteConnection conn, SqliteTransaction? tx, string id)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, title, copies FROM books WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Book(r.GetString(0), r.GetString(1), r.GetInt32(2));
    }

    public int CountActiveLoansForBook(string bookId)
    {
        using var conn = Open();
        return CountActiveLoansForBook(conn, null, bookId);
    }

    private static int CountActiveLoansForBook(SqliteConnection conn, SqliteTransaction? tx, string bookId)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM loans WHERE book_id = $b AND status = 'active'";
        cmd.Parameters.AddWithValue("$b", bookId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ---- Members -----------------------------------------------------------

    public Member InsertMember(string name)
    {
        var id = Ids.NewMemberId();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO members (id, name) VALUES ($id, $name)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
        return new Member(id, name);
    }

    public bool MemberExists(string id)
    {
        using var conn = Open();
        return MemberExists(conn, null, id);
    }

    private static bool MemberExists(SqliteConnection conn, SqliteTransaction? tx, string id)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM members WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is not null;
    }

    // ---- Loans -------------------------------------------------------------

    /// <summary>
    /// 貸出作成。存在確認(404)・延滞/上限/在庫(409)を1トランザクション内で判定し、
    /// OK なら INSERT する(INV-1/INV-2 の原子性)。
    /// </summary>
    public LoanResult CreateLoan(string bookId, string memberId, DateTimeOffset loanedAtUtc)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var book = GetBook(conn, tx, bookId);
        bool memberExists = MemberExists(conn, tx, memberId);
        if (book is null || !memberExists)
            return LoanResult.NotFound();

        int bookActive = CountActiveLoansForBook(conn, tx, bookId);
        var memberActiveLoans = LoadActiveLoansForMember(conn, tx, memberId);

        var error = LendingRules.EvaluateLoan(book.Copies, bookActive, memberActiveLoans, loanedAtUtc);
        if (error != LendingError.None)
            return LoanResult.Blocked(error);

        var due = LendingRules.ComputeDueDate(loanedAtUtc);
        var loan = new Loan(
            Ids.NewLoanId(),
            bookId,
            memberId,
            loanedAtUtc,
            due,
            LoanStatus.Active,
            ReturnedAtUtc: null,
            FineAmount: null);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO loans (id, book_id, member_id, loaned_at_utc, due_date_utc, status, returned_at_utc, fine_amount)
                VALUES ($id, $book, $member, $loaned, $due, 'active', NULL, NULL)
                """;
            cmd.Parameters.AddWithValue("$id", loan.Id);
            cmd.Parameters.AddWithValue("$book", loan.BookId);
            cmd.Parameters.AddWithValue("$member", loan.MemberId);
            cmd.Parameters.AddWithValue("$loaned", Iso.Format(loan.LoanedAtUtc));
            cmd.Parameters.AddWithValue("$due", Iso.FormatDate(loan.DueDateUtc));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return LoanResult.Ok(loan);
    }

    /// <summary>
    /// 返却。存在(404)・状態(409 already_returned)・瞬時逆転(400)を判定し、
    /// OK なら status=returned + returnedAt + fine を1トランザクションで更新する(INV-4)。
    /// </summary>
    public ReturnResult ReturnLoan(string loanId, DateTimeOffset returnedAtUtc)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var loan = GetLoan(conn, tx, loanId);
        if (loan is null)
            return ReturnResult.NotFound();

        if (loan.Status == LoanStatus.Returned)
            return ReturnResult.AlreadyReturned();

        // returnedAtUtc < loanedAtUtc(瞬時比較)→ invalid_request。同一瞬時は受理。
        if (returnedAtUtc < loan.LoanedAtUtc)
            return ReturnResult.InvalidInstant();

        int fine = LendingRules.ComputeFine(returnedAtUtc, loan.DueDateUtc);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE loans
                SET status = 'returned', returned_at_utc = $ret, fine_amount = $fine
                WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$ret", Iso.Format(returnedAtUtc));
            cmd.Parameters.AddWithValue("$fine", fine);
            cmd.Parameters.AddWithValue("$id", loanId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();

        var updated = loan with
        {
            Status = LoanStatus.Returned,
            ReturnedAtUtc = returnedAtUtc,
            FineAmount = fine
        };
        return ReturnResult.Ok(updated);
    }

    public Loan? GetLoan(string id)
    {
        using var conn = Open();
        return GetLoan(conn, null, id);
    }

    private static Loan? GetLoan(SqliteConnection conn, SqliteTransaction? tx, string id)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, book_id, member_id, loaned_at_utc, due_date_utc, status, returned_at_utc, fine_amount
            FROM loans WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadLoan(r);
    }

    private static List<Loan> LoadActiveLoansForMember(SqliteConnection conn, SqliteTransaction? tx, string memberId)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, book_id, member_id, loaned_at_utc, due_date_utc, status, returned_at_utc, fine_amount
            FROM loans WHERE member_id = $m AND status = 'active'
            """;
        cmd.Parameters.AddWithValue("$m", memberId);
        using var r = cmd.ExecuteReader();
        var list = new List<Loan>();
        while (r.Read())
            list.Add(ReadLoan(r));
        return list;
    }

    /// <summary>
    /// 会員別貸出一覧(active + returned 全件)。
    /// 並び順: loanedAtUtc 瞬時昇順、同値は id 序数昇順(仕様 §2.6)。
    /// </summary>
    public List<Loan> ListLoansForMember(string memberId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, book_id, member_id, loaned_at_utc, due_date_utc, status, returned_at_utc, fine_amount
            FROM loans WHERE member_id = $m
            """;
        cmd.Parameters.AddWithValue("$m", memberId);
        using var r = cmd.ExecuteReader();
        var list = new List<Loan>();
        while (r.Read())
            list.Add(ReadLoan(r));

        // 瞬時昇順 → id 序数昇順。文字列ソートではなくパース後の瞬時で比較する。
        list.Sort(static (a, b) =>
        {
            int c = a.LoanedAtUtc.UtcDateTime.CompareTo(b.LoanedAtUtc.UtcDateTime);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Id, b.Id);
        });
        return list;
    }

    private static Loan ReadLoan(SqliteDataReader r)
    {
        var loanedAt = Iso.Parse(r.GetString(3));
        var due = Iso.ParseDate(r.GetString(4));
        var status = r.GetString(5) == "returned" ? LoanStatus.Returned : LoanStatus.Active;
        DateTimeOffset? returnedAt = r.IsDBNull(6) ? null : Iso.Parse(r.GetString(6));
        int? fine = r.IsDBNull(7) ? null : r.GetInt32(7);
        return new Loan(r.GetString(0), r.GetString(1), r.GetString(2), loanedAt, due, status, returnedAt, fine);
    }
}
