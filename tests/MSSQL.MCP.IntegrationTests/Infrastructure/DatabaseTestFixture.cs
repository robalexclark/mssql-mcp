using Microsoft.Data.Sqlite;
using MSSQL.MCP.Database;
using Xunit;

namespace MSSQL.MCP.IntegrationTests.Infrastructure;

/// <summary>
/// Test fixture that provides an in-memory SQLite database for integration tests.
/// </summary>
public class DatabaseTestFixture : IAsyncLifetime
{
    private SqliteConnection? _keepAliveConnection;

    public string ConnectionString { get; private set; } = string.Empty;
    public IDbConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Create shared in-memory database and keep connection open
        _keepAliveConnection = new SqliteConnection("Data Source=InMemoryDb;Mode=Memory;Cache=Shared");
        await _keepAliveConnection.OpenAsync();

        ConnectionString = _keepAliveConnection.ConnectionString;
        ConnectionFactory = new SqliteConnectionFactory(ConnectionString);

        await CreateTestDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_keepAliveConnection != null)
        {
            await _keepAliveConnection.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates the test schema and seed data.
    /// </summary>
    public async Task CreateTestDatabaseAsync()
    {
        await using var connection = await ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Email TEXT NOT NULL UNIQUE,
                CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                IsActive INTEGER DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                OrderDate TEXT DEFAULT CURRENT_TIMESTAMP,
                Total REAL NOT NULL,
                Status TEXT DEFAULT 'Pending',
                FOREIGN KEY(UserId) REFERENCES Users(Id)
            );

            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Price REAL NOT NULL,
                Category TEXT,
                InStock INTEGER DEFAULT 1
            );

            INSERT INTO Users (Name, Email) VALUES
                ('John Doe', 'john@example.com'),
                ('Jane Smith', 'jane@example.com'),
                ('Bob Johnson', 'bob@example.com');

            INSERT INTO Orders (UserId, Total, Status) VALUES
                (1, 99.99, 'Completed'),
                (1, 149.99, 'Pending'),
                (2, 75.50, 'Completed'),
                (3, 200.00, 'Cancelled');

            INSERT INTO Products (Name, Price, Category) VALUES
                ('Laptop', 999.99, 'Electronics'),
                ('Mouse', 29.99, 'Electronics'),
                ('Book', 15.99, 'Education');
        ";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Cleans up test data between tests.
    /// </summary>
    public async Task CleanupTestDataAsync()
    {
        await using var connection = await ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            DELETE FROM Orders;
            DELETE FROM Users;
            DELETE FROM Products;
            DELETE FROM sqlite_sequence WHERE name IN ('Orders','Users','Products');
        ";
        await command.ExecuteNonQueryAsync();
        await CreateTestDatabaseAsync();
    }
}
