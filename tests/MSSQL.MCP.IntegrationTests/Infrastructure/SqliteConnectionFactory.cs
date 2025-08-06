using System.Data.Common;
using Microsoft.Data.Sqlite;
using MSSQL.MCP.Database;

namespace MSSQL.MCP.IntegrationTests.Infrastructure;

/// <summary>
/// Simple connection factory for SQLite in-memory database used in tests.
/// </summary>
public class SqliteConnectionFactory(string connectionString) : IDbConnectionFactory
{
    private readonly string _connectionString = connectionString;

    public DbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await CreateOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
