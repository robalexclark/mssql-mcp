using Microsoft.Extensions.Logging.Abstractions;
using MSSQL.MCP.IntegrationTests.Infrastructure;
using MSSQL.MCP.Tools;
using Xunit;

namespace MSSQL.MCP.IntegrationTests.Tools;

/// <summary>
/// Integration tests for SqlExecutionTool that validate all MCP tools work correctly
/// with an in-memory SQL database.
/// </summary>
[Collection("Database")]
public class SqlExecutionToolTests(DatabaseTestFixture fixture) : IAsyncLifetime
{
    private readonly SqlExecutionTool _tool = new(fixture.ConnectionFactory, NullLogger<SqlExecutionTool>.Instance);

    public async Task InitializeAsync()
    {
        await fixture.CleanupTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region ExecuteSql Tests

    [Fact]
    public async Task ExecuteSql_SelectQuery_ReturnsFormattedResults()
    {
        // Act
        var result = await _tool.ExecuteSql("SELECT Id, Name, Email FROM Users ORDER BY Id");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("John Doe", result);
        Assert.Contains("jane@example.com", result);
        Assert.Contains("Bob Johnson", result);
        
        // Should contain table headers
        Assert.Contains("Id", result);
        Assert.Contains("Name", result);
        Assert.Contains("Email", result);
    }

    [Fact]
    public async Task ExecuteSql_WithClause_ReturnsResults()
    {
        // Act
        var result = await _tool.ExecuteSql(@"
            WITH ActiveUsers AS (
                SELECT Id, Name FROM Users WHERE IsActive = 1
            )
            SELECT * FROM ActiveUsers ORDER BY Id");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("John Doe", result);
        Assert.Contains("Jane Smith", result);
        Assert.Contains("Bob Johnson", result);
    }

    [Fact]
    public async Task ExecuteSql_InsertQuery_ReturnsRowsAffected()
    {
        // Act
        var result = await _tool.ExecuteSql("INSERT INTO Users (Name, Email) VALUES ('Test User', 'test@example.com')");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Rows affected: 1", result);
        Assert.Contains("successfully", result);
    }

    [Fact]
    public async Task ExecuteSql_UpdateQuery_ReturnsRowsAffected()
    {
        // Act
        var result = await _tool.ExecuteSql("UPDATE Users SET Name = 'Updated Name' WHERE Id = 1");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Rows affected: 1", result);
        Assert.Contains("successfully", result);
    }

    [Fact]
    public async Task ExecuteSql_DeleteQuery_ReturnsRowsAffected()
    {
        // Act
        var result = await _tool.ExecuteSql("DELETE FROM Orders WHERE Status = 'Cancelled'");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Rows affected: 1", result);
        Assert.Contains("successfully", result);
    }

    [Fact]
    public async Task ExecuteSql_CreateTable_ReturnsSuccess()
    {
        // Act
        var result = await _tool.ExecuteSql(@"
            CREATE TABLE TestTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL
            )");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("successfully", result);
    }

    [Fact]
    public async Task ExecuteSql_EmptyQuery_ReturnsError()
    {
        // Act
        var result = await _tool.ExecuteSql("");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Error: SQL query cannot be empty", result);
    }

    [Fact]
    public async Task ExecuteSql_InvalidQuery_ReturnsError()
    {
        // Act - Use valid T-SQL syntax but invalid operation to get SQL Server error
        var result = await _tool.ExecuteSql("SELECT * FROM InvalidTable123");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("SQL Error:", result);
    }

    [Fact]
    public async Task ExecuteSql_NonSqlInput_ReturnsValidationError()
    {
        // Act - Use invalid T-SQL syntax to trigger validation error
        var result = await _tool.ExecuteSql("INVALID SQL QUERY");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Error: Invalid T-SQL syntax", result);
        Assert.Contains("This tool only accepts valid Microsoft SQL Server T-SQL statements", result);
    }

    [Fact]
    public async Task ExecuteSql_SelectFromNonExistentTable_ReturnsError()
    {
        // Act
        var result = await _tool.ExecuteSql("SELECT * FROM NonExistentTable");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("SQL Error:", result);
    }

    #endregion

    #region ListTables Tests

    [Fact]
    public async Task ListTables_ReturnsAllTables()
    {
        // Act
        var result = await _tool.ListTables();

        // Assert
        Assert.NotNull(result);
        
        // Should contain our test tables
        Assert.Contains("Users", result);
        Assert.Contains("Orders", result);
        Assert.Contains("Products", result);
        
        // Should show table type
        Assert.Contains("table", result);
    }

    [Fact]
    public async Task ListTables_ShowsCorrectColumns()
    {
        // Act
        var result = await _tool.ListTables();

        // Assert
        Assert.NotNull(result);
        
        // Should contain expected column headers
        Assert.Contains("TABLE_SCHEMA", result);
        Assert.Contains("TABLE_NAME", result);
        Assert.Contains("TABLE_TYPE", result);
    }

    #endregion



    #region ListSchemas Tests

    [Fact]
    public async Task ListSchemas_ReturnsAllSchemas()
    {
        // Act
        var result = await _tool.ListSchemas();

        // Assert
        Assert.NotNull(result);
        
        // SQLite has a single default schema
        Assert.Contains("main", result);
        
        // Should show column headers
        Assert.Contains("SCHEMA_NAME", result);
        Assert.Contains("SCHEMA_OWNER", result);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteSql_WithCancellation_HandlesGracefully()
    {
        using var cts = new CancellationTokenSource();
        
        // Start a query
        var task = _tool.ExecuteSql("SELECT 1", cts.Token);
        
        // Cancel immediately
        await cts.CancelAsync();
        
        // Should complete without throwing
        var result = await task;
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListTables_WithCancellation_HandlesGracefully()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        
        // Should handle cancellation gracefully
        var result = await _tool.ListTables(cts.Token);
        Assert.NotNull(result);
    }

    #endregion

    #region Data Validation Tests

    [Fact]
    public async Task ExecuteSql_ComplexJoin_ReturnsCorrectData()
    {
        // Act
        var result = await _tool.ExecuteSql(@"
            SELECT 
                u.Name as UserName,
                COUNT(o.Id) as OrderCount,
                SUM(o.Total) as TotalAmount
            FROM Users u
            LEFT JOIN Orders o ON u.Id = o.UserId
            WHERE u.IsActive = 1
            GROUP BY u.Id, u.Name
            ORDER BY u.Name");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("John Doe", result);
        Assert.Contains("Jane Smith", result);
        Assert.Contains("Bob Johnson", result);
        
        // Should show aggregated data
        Assert.Contains("249.98", result); // John's total orders
        Assert.Contains("75.5", result);  // Jane's total orders
        Assert.Contains("200", result); // Bob's total orders
    }

    [Fact]
    public async Task ExecuteSql_ParameterizedQuery_WorksCorrectly()
    {
        // Note: This test shows that regular parameterized queries work
        // In a real MCP implementation, you might want to add parameter support
        var result = await _tool.ExecuteSql("SELECT * FROM Users WHERE Id = 1");

        Assert.NotNull(result);
        Assert.Contains("John Doe", result);
        Assert.DoesNotContain("Jane Smith", result);
    }

    #endregion
} 