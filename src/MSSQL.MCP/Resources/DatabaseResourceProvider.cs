using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MSSQL.MCP.Database;

namespace MSSQL.MCP.Resources;

/// <summary>
/// Provides MCP resources representing the databases available on the connected
/// Microsoft SQL Server instance.
/// </summary>
[McpServerResourceProvider]
public sealed class DatabaseResourceProvider : IMcpServerResourceProvider
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseResourceProvider> _logger;

    public DatabaseResourceProvider(IDbConnectionFactory connectionFactory, ILogger<DatabaseResourceProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Lists databases as MCP resources using the <c>mssql://</c> URI scheme.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>An async sequence of <see cref="Resource"/> objects.</returns>
    public async IAsyncEnumerable<Resource> ListResourcesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.databases ORDER BY name";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            yield return new Resource
            {
                Name = name,
                Uri = $"mssql://{name}",
                Description = $"Microsoft SQL Server database '{name}'"
            };
        }
    }
}
