﻿using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MSSQL.MCP.Database;
using System.ComponentModel;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace MSSQL.MCP.Tools;

[McpServerToolType]
public class SqlExecutionTool(IDbConnectionFactory connectionFactory, ILogger<SqlExecutionTool> logger)
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<SqlExecutionTool> _logger = logger;

    // Regex to detect valid T-SQL keywords at the beginning of queries
    private static readonly Regex ValidTSqlStartPattern = new(
        @"^\s*(SELECT|INSERT|UPDATE|DELETE|WITH|CREATE|ALTER|DROP|GRANT|REVOKE|EXEC|EXECUTE|DECLARE|SET|USE|BACKUP|RESTORE|TRUNCATE|MERGE)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [McpServerTool, Description(@"Execute T-SQL queries against the connected Microsoft SQL Server database.
    
IMPORTANT: This tool ONLY accepts valid T-SQL (Transact-SQL) syntax for Microsoft SQL Server.

Supported operations:
- SELECT statements for data retrieval
- INSERT, UPDATE, DELETE for data modification  
- CREATE, ALTER, DROP for schema changes
- WITH clauses for CTEs (Common Table Expressions)
- EXEC/EXECUTE for stored procedures
- And other valid T-SQL statements

Examples of valid T-SQL:
- SELECT * FROM Users WHERE Active = 1
- INSERT INTO Products (Name, Price) VALUES ('Widget', 19.99)
- UPDATE Customers SET Status = 'Active' WHERE ID = 123
- CREATE TABLE Orders (ID int PRIMARY KEY, CustomerID int)

The query parameter must contain ONLY the T-SQL statement - no explanations, markdown, or other text.")]
    public async Task<string> ExecuteSql(
        [Description(@"The T-SQL query to execute. Must be valid Microsoft SQL Server T-SQL syntax only.
        Examples: 'SELECT * FROM Users', 'INSERT INTO Products VALUES (1, ''Name'')', 'CREATE TABLE Test (ID int)'
        Do NOT include explanations, markdown formatting, or non-SQL text.")]
        string query, CancellationToken cancellationToken = default)
    {
        // Log the incoming query for debugging
        _logger.LogInformation("Received SQL execution request. Query length: {QueryLength} characters", query.Length);
        _logger.LogDebug("SQL Query received: {Query}", query);

        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("Empty or null query received");
            return "Error: SQL query cannot be empty";
        }

        // Validate that the query looks like T-SQL
        var trimmedQuery = query.Trim();
        if (!ValidTSqlStartPattern.IsMatch(trimmedQuery))
        {
            _logger.LogWarning("Invalid T-SQL query received. Query does not start with valid T-SQL keyword: {QueryStart}",
                trimmedQuery.Length > 50 ? trimmedQuery[..50] + "..." : trimmedQuery);

            return @"Error: Invalid T-SQL syntax. This tool only accepts valid Microsoft SQL Server T-SQL statements.

Valid T-SQL statements must start with keywords like:
- SELECT (for data retrieval)
- INSERT, UPDATE, DELETE (for data modification)  
- CREATE, ALTER, DROP (for schema changes)
- WITH (for CTEs)
- EXEC/EXECUTE (for stored procedures)
- And other valid T-SQL keywords

Examples:
✓ SELECT * FROM Users
✓ INSERT INTO Products (Name) VALUES ('Test')
✓ CREATE TABLE Orders (ID int)

✗ Please show me all users
✗ Can you create a table for orders?
✗ ```sql SELECT * FROM Users```

Please provide only the T-SQL statement without explanations or formatting.";
        }

        try
        {
            _logger.LogInformation("Executing T-SQL query starting with: {QueryStart}",
                trimmedQuery.Length > 30 ? trimmedQuery[..30] + "..." : trimmedQuery);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = query;

            // Determine if this is a SELECT query or a command
            var isSelectQuery = trimmedQuery.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                               trimmedQuery.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

            if (isSelectQuery)
            {
                // Handle SELECT queries - return data
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var result = await FormatQueryResults(reader, cancellationToken);
                _logger.LogInformation("SELECT query executed successfully");
                return result;
            }
            else
            {
                // Handle INSERT/UPDATE/DELETE/DDL - return affected rows
                var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                var result = $"Query executed successfully. Rows affected: {rowsAffected}";
                _logger.LogInformation("Non-SELECT query executed successfully. Rows affected: {RowsAffected}", rowsAffected);
                return result;
            }
        }
        catch (DbException ex)
        {
            _logger.LogError(ex, "SQL execution failed with database error: {ErrorMessage}", ex.Message);
            return $"SQL Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL execution failed with general error: {ErrorMessage}", ex.Message);
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description("List all tables in accessible databases with basic information.")]
    public async Task<string> ListTables(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            string query;
            if (string.Equals(connection.GetType().Namespace, typeof(SqlConnection).Namespace, StringComparison.Ordinal))
            {
                // Get all non-system databases the user has access to
                var databaseNames = new List<string>();
                await using (var dbCommand = connection.CreateCommand())
                {
                    dbCommand.CommandText = "SELECT name FROM sys.databases WHERE database_id > 4 AND HAS_DBACCESS(name) = 1 ORDER BY name";
                    await using var dbReader = await dbCommand.ExecuteReaderAsync(cancellationToken);
                    while (await dbReader.ReadAsync(cancellationToken))
                    {
                        databaseNames.Add(dbReader.GetString(0));
                    }
                }

                // Build a UNION ALL query across all databases with database context
                var sb = new StringBuilder();
                for (int i = 0; i < databaseNames.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.AppendLine("UNION ALL");
                    }

                    var dbName = databaseNames[i];
                    var escapedDbName = dbName.Replace("]", "]]", StringComparison.Ordinal);
                    sb.Append($@"SELECT '{dbName}' AS DatabaseName,
                                s.name AS SchemaName,
                                t.name AS TableName,
                                ISNULL(p.rows, 0) AS RowCount,
                                t.type_desc AS TableType
                            FROM [{escapedDbName}].sys.tables t
                            JOIN [{escapedDbName}].sys.schemas s ON t.schema_id = s.schema_id
                            LEFT JOIN (
                                SELECT object_id, SUM(rows) AS rows
                                FROM [{escapedDbName}].sys.partitions
                                WHERE index_id IN (0,1)
                                GROUP BY object_id
                            ) p ON t.object_id = p.object_id");
                }
                sb.AppendLine("ORDER BY DatabaseName, SchemaName, TableName");
                query = sb.ToString();
            }
            else
            {
                // Fallback for providers that do not support sys.databases
                query = "SELECT 'main' AS DatabaseName, '' AS SchemaName, name AS TableName, 0 AS RowCount, '' AS TableType FROM sqlite_master WHERE type = 'table' ORDER BY name";
            }

            await using var command = connection.CreateCommand();
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await FormatQueryResults(reader, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Error listing tables: {ex.Message}";
        }
    }

    [McpServerTool, Description("List all databases and schemas available to the connection.")]
    public async Task<string> ListSchemas(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            string query;
            if (string.Equals(connection.GetType().Namespace, typeof(SqlConnection).Namespace, StringComparison.Ordinal))
            {
                // Get all databases the user has access to
                var databaseNames = new List<string>();
                await using (var dbCommand = connection.CreateCommand())
                {
                    dbCommand.CommandText = "SELECT name FROM sys.databases WHERE HAS_DBACCESS(name) = 1 ORDER BY name";
                    await using var dbReader = await dbCommand.ExecuteReaderAsync(cancellationToken);
                    while (await dbReader.ReadAsync(cancellationToken))
                    {
                        databaseNames.Add(dbReader.GetString(0));
                    }
                }

                // Build a UNION ALL query across all databases with database context
                var sb = new StringBuilder();
                for (int i = 0; i < databaseNames.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.AppendLine("UNION ALL");
                    }

                    var dbName = databaseNames[i].Replace("]", "]]", StringComparison.Ordinal);
                    sb.Append($@"SELECT '{databaseNames[i]}' AS DATABASE_NAME,
                                SCHEMA_NAME,
                                SCHEMA_OWNER,
                                DEFAULT_CHARACTER_SET_CATALOG,
                                DEFAULT_CHARACTER_SET_SCHEMA,
                                DEFAULT_CHARACTER_SET_NAME
                             FROM [{dbName}].INFORMATION_SCHEMA.SCHEMATA");
                }
                sb.AppendLine(" ORDER BY DATABASE_NAME, SCHEMA_NAME");
                query = sb.ToString();
            }
            else
            {
                query = "SELECT 'main' AS DATABASE_NAME, 'main' AS SCHEMA_NAME, '' AS SCHEMA_OWNER";
            }

            await using var command = connection.CreateCommand();
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await FormatQueryResults(reader, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Error listing schemas: {ex.Message}";
        }
    }

    private static async Task<string> FormatQueryResults(DbDataReader reader, CancellationToken cancellationToken)
    {
        var result = new System.Text.StringBuilder();

        if (!reader.HasRows)
        {
            return "Query executed successfully. No rows returned.";
        }

        // Get column headers
        var columnCount = reader.FieldCount;
        var columnNames = new string[columnCount];
        var columnWidths = new int[columnCount];

        for (int i = 0; i < columnCount; i++)
        {
            columnNames[i] = reader.GetName(i);
            columnWidths[i] = Math.Max(columnNames[i].Length, 10); // Minimum width of 10
        }

        // Read all rows to determine column widths
        var rows = new List<object[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                row[i] = await reader.IsDBNullAsync(i) ? "NULL" : reader.GetValue(i);
                var valueLength = row[i].ToString()?.Length ?? 4;
                columnWidths[i] = Math.Max(columnWidths[i], valueLength);
            }
            rows.Add(row);
        }

        // Build header
        result.Append(string.Join(" | ", columnNames.Select((name, i) => name.PadRight(columnWidths[i])))).Append("\n");
        result.Append(string.Join("-+-", columnWidths.Select(w => new string('-', w)))).Append("\n");

        // Build data rows
        foreach (var row in rows)
        {
            result.Append(string.Join(" | ", row.Select((value, i) =>
                (value.ToString() ?? "NULL").PadRight(columnWidths[i])))).Append("\n");
        }

        result.Append($"\n({rows.Count} row(s) returned)").Append("\n");

        return result.ToString();
    }
}