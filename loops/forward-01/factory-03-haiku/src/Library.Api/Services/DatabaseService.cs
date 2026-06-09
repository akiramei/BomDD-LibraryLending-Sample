using Microsoft.Data.Sqlite;

namespace Library.Api.Services;

/// <summary>
/// SQLite persistence service (E-SQLITE-STORE-001, K-SQLITE-001)
/// </summary>
public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(IConfiguration configuration)
    {
        var dbPath = Environment.GetEnvironmentVariable("LIBRARY_DB_PATH") ?? "./library.db";
        _connectionString = $"Data Source={dbPath}";
    }

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Create tables if they don't exist
        var command = connection.CreateCommand();
        command.CommandText = @"
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
                FineAmount INTEGER,
                FOREIGN KEY (BookId) REFERENCES Books(Id),
                FOREIGN KEY (MemberId) REFERENCES Members(Id)
            );
        ";
        command.ExecuteNonQuery();
    }

    public SqliteConnection GetConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
