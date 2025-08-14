#nullable disable
using Microsoft.Extensions.Logging.Abstractions;
using MSSQL.MCP.Database;
using MSSQL.MCP.Tools;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MSSQL.MCP.IntegrationTests.Tools
{
    public class SqlExecutionToolTests
    {
        [Fact]
        public async Task ListTables_ReturnsTablesFromMultipleDatabasesAndSchemas()
        {
            var tables = new Dictionary<string, List<(string Schema, string Table)>>
            {
                ["DbOne"] = new() { ("dbo", "Users"), ("sales", "Orders") },
                ["DbTwo"] = new() { ("dbo", "Customers"), ("hr", "Employees") }
            };

            var factory = new FakeSqlConnectionFactory(tables);
            var tool = new SqlExecutionTool(factory, NullLogger<SqlExecutionTool>.Instance);

            var result = await tool.ListTables();

            // Parse the tabular output into rows for easier assertions
            var rows = result
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(2) // Skip header and separator
                .Where(l => !l.StartsWith("("))
                .Select(l => l.Split('|').Select(c => c.Trim()).ToArray())
                .ToList();

            Assert.Contains(rows, r => r[0] == "DbOne" && r[1] == "dbo" && r[2] == "Users");
            Assert.Contains(rows, r => r[0] == "DbOne" && r[1] == "sales" && r[2] == "Orders");
            Assert.Contains(rows, r => r[0] == "DbTwo" && r[1] == "dbo" && r[2] == "Customers");
            Assert.Contains(rows, r => r[0] == "DbTwo" && r[1] == "hr" && r[2] == "Employees");

            Assert.Contains("DatabaseName", result);
            Assert.Contains("SchemaName", result);
            Assert.Contains("TableName", result);
        }

        [Fact]
        public async Task ExecuteSql_SelectQuery_ReturnsFormattedResults()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Rows.Add(1, "Alice");

            var factory = new FakeExecuteSqlConnectionFactory(table);
            var tool = new SqlExecutionTool(factory, NullLogger<SqlExecutionTool>.Instance);

            var result = await tool.ExecuteSql("SELECT Id, Name FROM Users");

            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var header = lines[0];
            var data = lines[2].Split('|').Select(c => c.Trim()).ToArray();

            Assert.Contains("Id", header);
            Assert.Contains("Name", header);
            Assert.Equal("1", data[0]);
            Assert.Equal("Alice", data[1]);
        }

        [Fact]
        public async Task ExecuteSql_NonSelectQuery_ReturnsRowsAffected()
        {
            var factory = new FakeExecuteSqlConnectionFactory(new DataTable(), rowsAffected: 2);
            var tool = new SqlExecutionTool(factory, NullLogger<SqlExecutionTool>.Instance);

            var result = await tool.ExecuteSql("UPDATE Users SET Name = 'Bob' WHERE Id = 1");

            Assert.Equal("Query executed successfully. Rows affected: 2", result);
        }

        [Fact]
        public async Task ExecuteSql_InvalidQuery_ReturnsError()
        {
            var factory = new FakeExecuteSqlConnectionFactory(new DataTable());
            var tool = new SqlExecutionTool(factory, NullLogger<SqlExecutionTool>.Instance);

            var result = await tool.ExecuteSql("Tell me all users");

            Assert.Contains("Invalid T-SQL syntax", result);
        }


        private class FakeSqlConnectionFactory : IDbConnectionFactory
        {
            private readonly IReadOnlyDictionary<string, List<(string Schema, string Table)>> _tables;

            public FakeSqlConnectionFactory(IReadOnlyDictionary<string, List<(string Schema, string Table)>> tables)
            {
                _tables = tables;
            }

            public DbConnection CreateConnection() => new Microsoft.Data.SqlClient.FakeSqlConnection(_tables);

            public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<DbConnection>(new Microsoft.Data.SqlClient.FakeSqlConnection(_tables));

            public Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(true);
        }

        private class FakeExecuteSqlConnectionFactory : IDbConnectionFactory
        {
            private readonly DataTable _result;
            private readonly int _rowsAffected;

            public FakeExecuteSqlConnectionFactory(DataTable result, int rowsAffected = 0)
            {
                _result = result;
                _rowsAffected = rowsAffected;
            }

            public DbConnection CreateConnection()
                => new Microsoft.Data.SqlClient.FakeExecuteSqlConnection(_result, _rowsAffected);

            public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<DbConnection>(new Microsoft.Data.SqlClient.FakeExecuteSqlConnection(_result, _rowsAffected));

            public Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(true);
        }
    }
}

namespace Microsoft.Data.SqlClient
{
    internal class FakeSqlConnection : DbConnection
    {
        private readonly Queue<DataTable> _tables;
        private ConnectionState _state = ConnectionState.Closed;

        public FakeSqlConnection(IReadOnlyDictionary<string, List<(string Schema, string Table)>> tables)
        {
            var dbTable = new DataTable();
            dbTable.Columns.Add("name", typeof(string));
            foreach (var db in tables.Keys)
            {
                dbTable.Rows.Add(db);
            }

            var tableTable = new DataTable();
            tableTable.Columns.Add("DatabaseName", typeof(string));
            tableTable.Columns.Add("SchemaName", typeof(string));
            tableTable.Columns.Add("TableName", typeof(string));
            tableTable.Columns.Add("RowCount", typeof(int));
            tableTable.Columns.Add("TableType", typeof(string));
            foreach (var kvp in tables)
            {
                foreach (var (schema, table) in kvp.Value)
                {
                    tableTable.Rows.Add(kvp.Key, schema, table, 0, "BASE TABLE");
                }
            }

            _tables = new Queue<DataTable>(new[] { dbTable, tableTable });
        }

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "master";
        public override string DataSource => "in-memory";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotImplementedException();

        protected override DbCommand CreateDbCommand()
        {
            var table = _tables.Dequeue();
            return new FakeSqlCommand(table);
        }
    }

    internal class FakeSqlCommand : DbCommand
    {
        private readonly DataTable _table;

        public FakeSqlCommand(DataTable table)
        {
            _table = table;
        }

        public override string CommandText { get; set; }
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object ExecuteScalar() => null;
        public override void Prepare() { }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => _table.CreateDataReader();

        protected override DbParameter CreateDbParameter()
            => new FakeDbParameter();
    }

    internal class FakeParameterCollection : DbParameterCollection
    {
        public override int Add(object value) => throw new NotImplementedException();
        public override void AddRange(Array values) => throw new NotImplementedException();
        public override void Clear() { }
        public override bool Contains(object value) => false;
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) { }
        public override int Count => 0;
        public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
        public override int IndexOf(object value) => -1;
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => throw new NotImplementedException();
        public override bool IsFixedSize => false;
        public override bool IsReadOnly => false;
        public override bool IsSynchronized => false;
        public override void Remove(object value) => throw new NotImplementedException();
        public override void RemoveAt(int index) => throw new NotImplementedException();
        public override void RemoveAt(string parameterName) => throw new NotImplementedException();
        protected override DbParameter GetParameter(int index) => throw new NotImplementedException();
        protected override DbParameter GetParameter(string parameterName) => throw new NotImplementedException();
        protected override void SetParameter(int index, DbParameter value) => throw new NotImplementedException();
        protected override void SetParameter(string parameterName, DbParameter value) => throw new NotImplementedException();
        public override object SyncRoot => new object();
    }

    internal class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; }
        public override string SourceColumn { get; set; }
        public override object Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    internal class FakeExecuteSqlConnection : DbConnection
    {
        private readonly DataTable _result;
        private readonly int _rowsAffected;
        private ConnectionState _state = ConnectionState.Closed;

        public FakeExecuteSqlConnection(DataTable result, int rowsAffected)
        {
            _result = result;
            _rowsAffected = rowsAffected;
        }

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "master";
        public override string DataSource => "in-memory";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotImplementedException();

        protected override DbCommand CreateDbCommand() => new FakeExecuteSqlCommand(_result, _rowsAffected);
    }

    internal class FakeExecuteSqlCommand : DbCommand
    {
        private readonly DataTable _result;
        private readonly int _rowsAffected;

        public FakeExecuteSqlCommand(DataTable result, int rowsAffected)
        {
            _result = result;
            _rowsAffected = rowsAffected;
        }

        public override string CommandText { get; set; }
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();
        protected override DbTransaction DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => _rowsAffected;
        public override object ExecuteScalar() => null;
        public override void Prepare() { }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => _result.CreateDataReader();

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
    }
}