using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSSQL.MCP.Database;

namespace MSSQL.MCP;

public static class Program
{
    /// <summary>
    /// Entry point for the MCP server application.
    /// Sets up logging, configures the MCP server with standard I/O transport and tool discovery,
    /// builds the host, and runs the server asynchronously.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static async Task Main(string[] args)
    {
        // Create the application host builder
        var builder = Host.CreateApplicationBuilder(args);

        // Configure console logging with Trace level
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        // Retrieve connection string environment variables
        var connectionNamesVar = Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING_ENV_NAMES");
        var connectionNames = string.IsNullOrWhiteSpace(connectionNamesVar)
            ? new[] { "MSSQL_CONNECTION_STRING" }
            : connectionNamesVar.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var connections = new List<(string Name, string ConnectionString)>();
        foreach (var name in connectionNames)
        {
            var cs = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(cs))
            {
                await Console.Error.WriteLineAsync($"Error: {name} environment variable is not set.");
                Environment.Exit(1);
                return;
            }
            connections.Add((name, cs));
        }

        // Register a default IDbConnectionFactory and keyed factories for each connection
        var defaultConnection = connections[0];
        builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqlConnectionFactory(defaultConnection.ConnectionString));

        foreach (var (name, cs) in connections)
        {
            builder.Services.AddKeyedSingleton<IDbConnectionFactory>(name, (_, _) => new SqlConnectionFactory(cs));
        }

        // Register MCP server and tools (instance-based)
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        // Build the host
        var host = builder.Build();

        // Test all database connections before running the host
        foreach (var (name, _) in connections)
        {
            IDbConnectionFactory dbFactory = name == defaultConnection.Name
                ? host.Services.GetRequiredService<IDbConnectionFactory>()
                : host.Services.GetRequiredKeyedService<IDbConnectionFactory>(name);

            try
            {
                var isValid = await dbFactory.ValidateConnectionAsync();
                if (!isValid)
                {
                    await Console.Error.WriteLineAsync($"Database connection test failed for '{name}'.");
                    Environment.Exit(1);
                    return;
                }

                Console.WriteLine($"Database connection test succeeded for '{name}'.");
            }
            catch (Exception dbEx)
            {
                await Console.Error.WriteLineAsync($"Database connection test failed for '{name}': {dbEx.Message}");
                Environment.Exit(1);
                return;
            }
        }


        // Setup cancellation token for graceful shutdown (Ctrl+C or SIGTERM)
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true; // Prevent the process from terminating immediately
            cts.Cancel();
        };

        try
        {
            // Run the host with cancellation support
            await host.RunAsync(cts.Token);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Unhandled exception: {ex}");
            Environment.ExitCode = 1;
        }
    }
}