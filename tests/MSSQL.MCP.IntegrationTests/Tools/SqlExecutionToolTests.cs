#nullable disable
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MSSQL.MCP.Database;
using MSSQL.MCP.Tools;
using Xunit;

namespace MSSQL.MCP.IntegrationTests.Tools
{
    public class SqlExecutionToolTests
    {
        [Fact]
        public async Task ListTables_ReturnsTablesFromMultipleDatabases()
        {
            var tables = new Dictionary<string, List<(string Schema, string Table)>>
            {
                ["DbOne"] = new() { ("dbo", "Users"), ("custom", "Logs") },
                ["DbTwo"] = new() { ("dbo", "Orders") }
            };
            var factory = new FakeSqlConnectionFactory(tables);
            var tool = new SqlExecutionTool(factory, NullLogger<SqlExecutionTool>.Instance);

            var result = await tool.ListTables();

            Assert.Contains("DbOne", result);
            Assert.Contains("DbTwo", result);
            Assert.Contains("Users", result);
            Assert.Contains("Orders", result);
            Assert.Contains("DatabaseName", result);
            Assert.Contains("TableName", result);
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
}