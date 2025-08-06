using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSSQL.MCP.Configuration;
using MSSQL.MCP.Database;

var hostBuilder = new HostBuilder();

hostBuilder
    .ConfigureAppConfiguration((context, builder) =>
    {
        builder.AddEnvironmentVariables();
        // Map MSSQL_CONNECTION_STRING to Database:ConnectionString
        builder.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Database:ConnectionString", 
                Environment.GetEnvironmentVariable("MSSQL_CONNECTION_STRING"))
        ]);
    })
    .ConfigureServices((context, services) =>
{
    // Configure logging to stderr for MCP protocol compatibility
    services.AddLogging(builder =>
    {
        builder.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = Microsoft.Extensions.Logging.LogLevel.Trace;
        });
    });

    // Configure Database options with validation
    services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
    services.AddOptionsWithValidateOnStart<DatabaseOptions>()
        .BindConfiguration("Database");

    // Register SQL Connection Factory
    services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

    // Add MCP Server
    services.AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
});

var host = hostBuilder.Build();

await host.RunAsync();
