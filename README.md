# mssql-mcp

A .NET-powered Model Context Protocol (MCP) server for Microsoft SQL Server.

## Abstract

Why does this exist? Because the other MCP solutions in market for this are generally janky pieces of shit that don't work - certainly not on Windows.

This MCP server provides AI agents with robust, reliable access to Microsoft SQL Server databases through a clean, well-architected .NET application built with the official MCP C# SDK.

## Features

- **Schema Discovery**: AI agents can explore database structure without writing complex SQL
- **Query Execution**: Full SQL support for SELECT, INSERT, UPDATE, DELETE, and DDL operations
- **Error Handling**: Comprehensive error handling with clear, actionable error messages
- **Table Formatting**: Query results formatted in readable tables for AI consumption
- **Docker Support**: Easy deployment with built-in .NET Docker tooling

## Available Tools

| Tool | Description |
|------|-------------|
| `execute_sql` | Execute any SQL query against the database |
| `list_tables` | List all tables with schema, name, type, and row count |
| `list_schemas` | List all available schemas/databases in the SQL Server instance |

## Configuration

### Required Environment Variables

The MCP server requires a single environment variable:

- **`MSSQL_CONNECTION_STRING`**: Complete SQL Server connection string

#### Example Connection Strings

**Windows Authentication:**
```
MSSQL_CONNECTION_STRING="Server=localhost;Database=MyDatabase;Trusted_Connection=true;"
```

**SQL Server Authentication:**
```
MSSQL_CONNECTION_STRING="Server=localhost;Database=MyDatabase;User Id=myuser;Password=mypassword;"
```

**Azure SQL Database:**
```
MSSQL_CONNECTION_STRING="Server=myserver.database.windows.net;Database=mydatabase;User Id=myuser;Password=mypassword;Encrypt=true;"
```

## Running the MCP Server

### Option 1: Docker (Recommended)

The easiest way to run the MCP server is using Docker with .NET's built-in container support.

#### Build and Run with Docker

Clone the repository

```bash
# Clone the repository
git clone https://github.com/Aaronontheweb/mssql-mcp.git
cd mssql-mcp
```

Build the Docker image

```bash
dotnet publish --os linux --arch x64 /t:PublishContainer
```

You can run the container directly if you wish, but it's **probably best** to let the MCP server spin up the client:

```bash
# Run the container
docker run -it --rm \
  -e MSSQL_CONNECTION_STRING="Server=host.docker.internal;Database=MyDB;Trusted_Connection=true;" \
  mssql-mcp:latest
```

## MCP Client Configuration

### Cursor IDE

Add to your Cursor settings (`Cursor Settings > Features > Model Context Protocol`):

```json
{
  "mcpServers": {
    "mssql": {
      "command": "docker",
      "args": [
          "run",
          "-i",
          "--rm",
          "-e",
          "MSSQL_CONNECTION_STRING",
          "mssql-mcp:latest"
      ],
      "env": {
          "MSSQL_CONNECTION_STRING": "Server=host.docker.internal,1533; Database=MyDb; User Id=myUser; Password=My(!)Password;TrustServerCertificate=true;"
      }
    }
  }
}
```

### Claude Desktop

Add to your Claude Desktop configuration file:

* **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`
* **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "mssql": {
      "command": "docker",
      "args": [
          "run",
          "-i",
          "--rm",
          "-e",
          "MSSQL_CONNECTION_STRING",
          "mssql-mcp:latest"
      ],
      "env": {
          "MSSQL_CONNECTION_STRING": "Server=host.docker.internal,1533; Database=MyDb; User Id=myUser; Password=My(!)Password;TrustServerCertificate=true;"
      }
    }
  }
}
```

You might need to create that file and restart Claude Desktop for the changes to take effect.

#### Understanding Your Claude Desktop MCP Server Configuration

This JSON configuration is for **Claude Desktop's Model Context Protocol (MCP) servers**. It essentially teaches Claude how to connect to and use a custom 
"tool" that interacts with a **Microsoft SQL Server (MSSQL)** database.

Let's break down each part:

##### `mcpServers`

This is the top-level section where you define all your custom MCP servers. You can set up multiple servers here, each with its own unique name.

##### `"mssql"`

This is the **unique name** you've chosen for this particular SQL Server integration. Claude will use this name to refer to this database connection.

##### `"command": "docker"`

This line tells Claude Desktop to launch the MCP server using **Docker**. This means the actual server software runs inside an isolated container, 
and you'll need **Docker Desktop** installed and running on your Windows/mac/Linux machine for this to work. Alternatively, you can use remote Docker server
using [custon context](https://docs.docker.com/engine/manage-resources/contexts/).

##### `"args": [...]`

These are the **arguments** Claude Desktop passes to the `docker` command when starting the container:

* `"run"`: This standard Docker command creates and starts a new container.
* `"-i"`: Stands for "interactive," keeping the standard input open for communication between the MCP server and Claude Desktop.
* `"--rm"`: This important argument tells Docker to **automatically remove the container** when it stops. This helps keep your Docker environment tidy.
* `"-e", "MSSQL_CONNECTION_STRING"`: This passes an **environment variable** named `MSSQL_CONNECTION_STRING` into the Docker container.
* `"mssql-mcp:latest"`: This specifies the **Docker image** to use. This image (`mssql-mcp` with the `latest` tag) contains the actual MCP server application 
   designed to interact with SQL Server. You'll need to ensure this image is available (either built locally or pulled from a Docker registry).

##### `"env": {...}`

This section defines the **environment variables** that will be set when Docker executes the command.

* `"MSSQL_CONNECTION_STRING": "Server=host.docker.internal,1533; Database=MyDb; User Id=myUser; Password=My(!)Password;TrustServerCertificate=true;"`
    * This is the **SQL Server connection string** that the `mssql-mcp` Docker container will use to connect to your database.
    * `Server=host.docker.internal,1533`: `host.docker.internal` is a special Docker DNS name that lets the container reach your **host machine's IP address**. 
       This is how the MCP server inside Docker can connect to your SQL Server instance, which is presumably running directly on your machine. `1533` is the 
       port your SQL Server is listening on.
    * `Database=MyDb`: The name of the specific database you want to connect to.
    * `User Id=myUser; Password=My(!)Password;`: The credentials for a user (`myUser`) to log into your SQL Server.
    * `TrustServerCertificate=true;`: This tells the client to **skip validating the server's SSL/TLS certificate**. While convenient for development or when 
      using self-signed certificates, be aware this reduces security by making you vulnerable to man-in-the-middle attacks in production environments.

---
##### In a Nutshell:

This configuration enables Claude Desktop to run a SQL Server-specific MCP server inside a Docker container. This server then uses the provided connection
string to establish a connection to your SQL Server database, allowing Claude to interact with your data through this custom tool.

### Local Binary Configuration

If running the built binary directly instead of Docker:

```json
{
  "mcpServers": {
    "mssql": {
      "command": "/path/to/mssql-mcp/src/MSSQL.MCP/bin/Release/net9.0/MSSQL.MCP",
      "env": {
        "MSSQL_CONNECTION_STRING": "Server=localhost;Database=MyDB;Trusted_Connection=true;"
      }
    }
  }
}
```

## Docker Networking Issues

### Understanding the Problem

When running the MCP server as a Docker container, you'll encounter networking challenges when trying to connect to SQL Server instances running on your host machine or in other containers. Docker containers are isolated from the host network by default, making `localhost` connections impossible.

### Solutions by Scenario

#### Scenario 1: SQL Server Running on Host Machine

**Problem**: Your SQL Server is installed directly on Windows/macOS/Linux, and you want the containerized MCP server to connect to it.

**Solution**: Use `host.docker.internal` instead of `localhost` in your connection string.

```bash
# ❌ This won't work - localhost refers to the container itself
docker run -it --rm \
  -e MSSQL_CONNECTION_STRING="Server=localhost;Database=MyDB;User Id=sa;Password=YourPassword123!;" \
  mssql-mcp:latest

# ✅ This works - host.docker.internal refers to the host machine
docker run -it --rm \
  -e MSSQL_CONNECTION_STRING="Server=host.docker.internal;Database=MyDB;User Id=sa;Password=YourPassword123!;" \
  mssql-mcp:latest
```

**Updated MCP Client Configuration:**
```json
{
  "mcpServers": {
    "mssql": {
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "-e", "MSSQL_CONNECTION_STRING=Server=host.docker.internal;Database=MyDB;User Id=sa;Password=YourPassword123!;",
        "mssql-mcp:latest"
      ]
    }
  }
}
```

#### Scenario 2: SQL Server in Another Docker Container

**Solution**: Use Docker Compose with a custom network and reference containers by service name.

```yaml
version: '3.8'
networks:
  sql-network:
    driver: bridge

services:
  mssql-mcp:
    build: .
    environment:
      # Use the service name 'sqlserver' as the hostname
      - MSSQL_CONNECTION_STRING=Server=sqlserver;Database=MyDatabase;User Id=sa;Password=YourPassword123!;
    stdin_open: true
    tty: true
    networks:
      - sql-network
    depends_on:
      - sqlserver
      
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourPassword123!
    networks:
      - sql-network
    ports:
      - "1433:1433"  # Expose to host for external tools
```

#### Scenario 3: Linux with Host Network Mode

**Linux Only Solution**: Use Docker's host networking mode for direct host network access.

```bash
# Linux only - shares the host's network stack
docker run -it --rm --network host \
  -e MSSQL_CONNECTION_STRING="Server=localhost;Database=MyDB;User Id=sa;Password=YourPassword123!;" \
  mssql-mcp:latest
```

### Platform-Specific Considerations

| Platform | host.docker.internal | Host Network Mode | Recommended Solution |
|----------|---------------------|-------------------|---------------------|
| **Windows** | ✅ Works out of box | ❌ Not supported | Use `host.docker.internal` |
| **macOS** | ✅ Works out of box | ❌ Not supported | Use `host.docker.internal` |
| **Linux** | ⚠️ Requires `--add-host` | ✅ Supported | Use `--network host` or `host.docker.internal` |

**Linux `host.docker.internal` setup:**
```bash
docker run -it --rm \
  --add-host=host.docker.internal:host-gateway \
  -e MSSQL_CONNECTION_STRING="Server=host.docker.internal;Database=MyDB;User Id=sa;Password=YourPassword123!;" \
  mssql-mcp:latest
```

### Testing Network Connectivity

To verify your container can reach the SQL Server:

```bash
# Test from inside a running container
docker exec -it <container_name> ping host.docker.internal

# Test SQL Server port specifically
docker run --rm -it mcr.microsoft.com/mssql-tools \
  /bin/bash -c "sqlcmd -S host.docker.internal -U sa -P 'YourPassword123!' -Q 'SELECT @@VERSION'"
```

### Common Networking Troubleshooting

1. **Connection Refused**: 
   - Verify SQL Server is listening on all interfaces: `netstat -an | grep 1433`
   - Check Windows Firewall allows Docker subnet access

2. **DNS Resolution**:
   - Test: `docker run --rm busybox nslookup host.docker.internal`
   - Ensure Docker Desktop is running (for Windows/macOS)

3. **Container-to-Container**:
   - Verify both containers are on the same Docker network
   - Use container service names, not localhost

4. **Port Conflicts**:
   - Ensure port 1433 isn't already bound by another process
   - Check with: `netstat -tlnp | grep 1433`

## Usage Examples

Once configured, AI agents can use natural language to interact with your database:

**"Show me all the tables in the database"**
→ Uses `list_tables` tool

**"Describe the structure of the Users table"**
→ Uses `execute_sql` with an INFORMATION_SCHEMA query

**"Find all users created in the last 30 days"**
→ Uses `execute_sql` with appropriate SELECT query

**"Create a new customer record"**
→ Uses `execute_sql` with INSERT statement

## Security Considerations

### ⚠️ Important Security Warnings

- **Database Permissions**: Only grant the minimum required permissions to the database user
- **Connection Security**: Use encrypted connections for production environments
- **Access Control**: This MCP server provides full SQL execution capabilities - ensure proper access controls
- **Audit Logging**: Consider enabling SQL Server audit logging for production use
- **Network Security**: Restrict network access to the database server appropriately

### Recommended Database Permissions

For read-only access:
```sql
-- Create a dedicated user with minimal permissions
CREATE LOGIN mcp_readonly WITH PASSWORD = 'SecurePassword123!';
CREATE USER mcp_readonly FOR LOGIN mcp_readonly;

-- Grant only necessary permissions
GRANT SELECT ON SCHEMA::dbo TO mcp_readonly;
GRANT VIEW DEFINITION ON SCHEMA::dbo TO mcp_readonly;
```

For read-write access:
```sql
-- Create a dedicated user
CREATE LOGIN mcp_readwrite WITH PASSWORD = 'SecurePassword123!';
CREATE USER mcp_readwrite FOR LOGIN mcp_readwrite;

-- Grant necessary permissions
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO mcp_readwrite;
GRANT VIEW DEFINITION ON SCHEMA::dbo TO mcp_readwrite;
```

## Troubleshooting

### Connection Issues

1. **Verify connection string**: Test with SQL Server Management Studio or Azure Data Studio
2. **Check firewall**: Ensure SQL Server port (default 1433) is accessible
3. **Enable TCP/IP**: Ensure TCP/IP protocol is enabled in SQL Server Configuration Manager
4. **Authentication mode**: Verify SQL Server is configured for the appropriate authentication mode

### Container Issues

1. **Network connectivity**: Use `host.docker.internal` instead of `localhost` when connecting from container to host
2. **Environment variables**: Ensure the connection string is properly escaped in Docker commands
3. **Logs**: Check container logs with `docker logs <container_id>`

## License

This software is licensed under Apache 2.0 and is available "as is" - this means that if you turbo-nuke your database because you gave an AI agent `sa` access through this MCP server, we're not responsible.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## Architecture

- **[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)**: Official Model Context Protocol implementation
- **Microsoft.Data.SqlClient**: High-performance SQL Server connectivity

