using System;
using MSSQL.MCP.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MSSQL.MCP.IntegrationTests.Database;

/// <summary>
/// Integration tests for SqlConnectionFactory to validate connection handling.
/// </summary>
[Collection("Database")]
public class SqlConnectionFactoryTests(DatabaseTestFixture fixture)
{
    [Fact]
    public void CreateConnection_ReturnsValidConnection()
    {
        // Act
        using var connection = fixture.ConnectionFactory.CreateConnection();

        // Assert
        Assert.NotNull(connection);
        Assert.IsType<SqliteConnection>(connection);
        Assert.Equal(fixture.ConnectionString, connection.ConnectionString);
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_ReturnsOpenConnection()
    {
        // Act
        await using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_WithCancellation_HandlesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Should handle cancellation token (TaskCanceledException inherits from OperationCanceledException)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync(cts.Token);
        });
    }

    [Fact]
    public async Task ValidateConnectionAsync_WithValidConnection_ReturnsTrue()
    {
        // Act
        var isValid = await fixture.ConnectionFactory.ValidateConnectionAsync();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateConnectionAsync_WithTimeout_CompletesQuickly()
    {
        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var isValid = await fixture.ConnectionFactory.ValidateConnectionAsync();
        stopwatch.Stop();

        // Assert
        Assert.True(isValid);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000); // Should complete within 5 seconds
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_MultipleConnections_WorkCorrectly()
    {
        // Act & Assert - Test connection pooling
        var tasks = new List<Task>();
        
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                var resultObj = await command.ExecuteScalarAsync();
                var result = Convert.ToInt64(resultObj);
                Assert.Equal(1L, result);
            }));
        }

        await Task.WhenAll(tasks);
    }
} 