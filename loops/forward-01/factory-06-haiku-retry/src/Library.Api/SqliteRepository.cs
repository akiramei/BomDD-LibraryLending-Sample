using System;
using System.Collections.Generic;
using Library.Core;
using Microsoft.Data.Sqlite;

namespace Library.Api;

public class SqliteRepository : ILoanRepository
{
    private readonly string _connectionString;

    public SqliteRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Books (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Copies INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Members (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Loans (
                Id TEXT PRIMARY KEY,
                BookId TEXT NOT NULL,
                MemberId TEXT NOT NULL,
                LoanedAtUtc TEXT NOT NULL,
                DueDateUtc TEXT NOT NULL,
                Status TEXT NOT NULL,
                ReturnedAtUtc TEXT,
                FineAmount INTEGER
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public void SaveBook(Book book)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Books (Id, Title, Copies)
            VALUES (@Id, @Title, @Copies)
        ";
        cmd.Parameters.AddWithValue("@Id", book.Id);
        cmd.Parameters.AddWithValue("@Title", book.Title);
        cmd.Parameters.AddWithValue("@Copies", book.Copies);
        cmd.ExecuteNonQuery();
    }

    public Book? GetBook(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Copies FROM Books WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var bookId = reader.GetString(0);
            var title = reader.GetString(1);
            var copies = reader.GetInt32(2);
            var activeLoanCount = GetActiveLoanCountForBook(bookId);
            var availableCopies = copies - activeLoanCount;

            return new Book(bookId, title, copies, availableCopies);
        }

        return null;
    }

    public void SaveMember(Member member)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Members (Id, Name)
            VALUES (@Id, @Name)
        ";
        cmd.Parameters.AddWithValue("@Id", member.Id);
        cmd.Parameters.AddWithValue("@Name", member.Name);
        cmd.ExecuteNonQuery();
    }

    public Member? GetMember(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name FROM Members WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new Member(reader.GetString(0), reader.GetString(1));
        }

        return null;
    }

    public void SaveLoan(LoanDto loan)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Loans (Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount)
            VALUES (@Id, @BookId, @MemberId, @LoanedAtUtc, @DueDateUtc, @Status, @ReturnedAtUtc, @FineAmount)
        ";
        cmd.Parameters.AddWithValue("@Id", loan.Id);
        cmd.Parameters.AddWithValue("@BookId", loan.BookId);
        cmd.Parameters.AddWithValue("@MemberId", loan.MemberId);
        cmd.Parameters.AddWithValue("@LoanedAtUtc", loan.LoanedAtUtc);
        cmd.Parameters.AddWithValue("@DueDateUtc", loan.DueDateUtc);
        cmd.Parameters.AddWithValue("@Status", loan.Status);
        cmd.Parameters.AddWithValue("@ReturnedAtUtc", (object?)loan.ReturnedAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FineAmount", (object?)loan.FineAmount ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public LoanDto? GetLoan(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount
            FROM Loans WHERE Id = @Id
        ";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new LoanDto(
                Id: reader.GetString(0),
                BookId: reader.GetString(1),
                MemberId: reader.GetString(2),
                LoanedAtUtc: reader.GetString(3),
                DueDateUtc: reader.GetString(4),
                Status: reader.GetString(5),
                ReturnedAtUtc: reader.IsDBNull(6) ? null : reader.GetString(6),
                FineAmount: reader.IsDBNull(7) ? null : reader.GetInt32(7)
            );
        }

        return null;
    }

    public List<LoanDto> GetLoansByMember(string memberId)
    {
        var loans = new List<LoanDto>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, BookId, MemberId, LoanedAtUtc, DueDateUtc, Status, ReturnedAtUtc, FineAmount
            FROM Loans WHERE MemberId = @MemberId
        ";
        cmd.Parameters.AddWithValue("@MemberId", memberId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            loans.Add(new LoanDto(
                Id: reader.GetString(0),
                BookId: reader.GetString(1),
                MemberId: reader.GetString(2),
                LoanedAtUtc: reader.GetString(3),
                DueDateUtc: reader.GetString(4),
                Status: reader.GetString(5),
                ReturnedAtUtc: reader.IsDBNull(6) ? null : reader.GetString(6),
                FineAmount: reader.IsDBNull(7) ? null : reader.GetInt32(7)
            ));
        }

        return loans;
    }

    public int GetActiveLoanCountForBook(string bookId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE BookId = @BookId AND Status = 'active'";
        cmd.Parameters.AddWithValue("@BookId", bookId);

        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    public int GetActiveLoanCountForMember(string memberId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Loans WHERE MemberId = @MemberId AND Status = 'active'";
        cmd.Parameters.AddWithValue("@MemberId", memberId);

        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 0;
    }
}
