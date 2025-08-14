﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSSQL.MCP.Database;
using System;
using System.Threading;
using System.Threading.Tasks;

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

        // Retrieve connection string environment variable
        var connectionString = Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            await Console.Error.WriteLineAsync("Error: MSSQL_CONNECTION_STRING environment variable is not set.");
            Environment.Exit(1);
            return;
        }

        // Register the IDbConnectionFactory for the single connection
        builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqlConnectionFactory(connectionString));

        // Register MCP server and tools (instance-based)
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        // Build the host
        var host = builder.Build();

        // Test the database connection before running the host
        var dbFactory = host.Services.GetRequiredService<IDbConnectionFactory>();
        try
        {
            var isValid = await dbFactory.ValidateConnectionAsync();
            if (!isValid)
            {
                await Console.Error.WriteLineAsync("Database connection test failed.");
                Environment.Exit(1);
                return;
            }

            Console.WriteLine("Database connection test succeeded.");
        }
        catch (Exception dbEx)
        {
            await Console.Error.WriteLineAsync($"Database connection test failed: {dbEx.Message}");
            Environment.Exit(1);
            return;
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