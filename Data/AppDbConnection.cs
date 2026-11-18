using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Dairy_Management_System.Data
{
    /// <summary>
    /// Managed ADO.NET connection wrapper that transparently enforces the least-privilege
    /// 'dms_app_user' database execution context while ensuring clean, reliable connection pool recycling
    /// by guaranteeing that 'REVERT;' is executed before the underlying physical connection is returned to the pool.
    /// Provides seamless implicit conversion to Microsoft.Data.SqlClient.SqlConnection for full ADO.NET interoperability.
    /// </summary>
    public sealed class AppDbConnection : DbConnection, IAsyncDisposable
    {
        private readonly SqlConnection _underlying;
        private bool _reverted;
        private bool _disposed;

        public SqlConnection Underlying => _underlying;

        public AppDbConnection(SqlConnection underlying)
        {
            _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        }

        public static implicit operator SqlConnection(AppDbConnection appConn)
        {
            ArgumentNullException.ThrowIfNull(appConn);
            return appConn._underlying;
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => _underlying.ConnectionString;
            set => _underlying.ConnectionString = value ?? string.Empty;
        }

        public override string Database => _underlying.Database;
        public override string DataSource => _underlying.DataSource;
        public override string ServerVersion => _underlying.ServerVersion;
        public override ConnectionState State => _underlying.State;

        public override void ChangeDatabase(string databaseName) => _underlying.ChangeDatabase(databaseName);
        public override void Open() => _underlying.Open();
        public override Task OpenAsync(CancellationToken cancellationToken) => _underlying.OpenAsync(cancellationToken);

        protected override DbCommand CreateDbCommand() => _underlying.CreateCommand();

        public new SqlCommand CreateCommand() => _underlying.CreateCommand();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            return _underlying.BeginTransaction(isolationLevel);
        }

        protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
        {
            return await _underlying.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }

        public new async Task<SqlTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
        {
            return (SqlTransaction)await _underlying.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }

        public new SqlTransaction BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return _underlying.BeginTransaction(isolationLevel);
        }

        public override void Close()
        {
            RevertContextSync();
            _underlying.Close();
        }

        public override async Task CloseAsync()
        {
            await RevertContextAsync().ConfigureAwait(false);
            await _underlying.CloseAsync().ConfigureAwait(false);
        }

        public override async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await RevertContextAsync().ConfigureAwait(false);
            await _underlying.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                RevertContextSync();
                _underlying.Dispose();
            }

            base.Dispose(disposing);
        }

        private async ValueTask RevertContextAsync()
        {
            if (_reverted) return;
            _reverted = true;

            if (_underlying.State == ConnectionState.Open)
            {
                try
                {
                    await using var cmd = new SqlCommand("IF USER_NAME() = 'dms_app_user' REVERT;", _underlying);
                    cmd.CommandType = CommandType.Text;
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Suppress if connection was severed or already closed
                }
            }
        }

        private void RevertContextSync()
        {
            if (_reverted) return;
            _reverted = true;

            if (_underlying.State == ConnectionState.Open)
            {
                try
                {
                    using var cmd = new SqlCommand("IF USER_NAME() = 'dms_app_user' REVERT;", _underlying);
                    cmd.CommandType = CommandType.Text;
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // Suppress if connection was severed or already closed
                }
            }
        }
    }
}
