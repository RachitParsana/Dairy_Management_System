using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Dairy_Management_System.Data
{
    /// <summary>
    /// Centralized SQL connection factory enforcing least-privilege database execution contexts
    /// for application operations while isolating administrative startup connections.
    /// </summary>
    public interface ISqlConnectionFactory
    {
        /// <summary>
        /// Creates, opens, and configures an application database connection bound to the
        /// least-privilege 'dms_app_user' principal (member of db_dms_app role).
        /// Returns a managed AppDbConnection wrapper that guarantees clean context reversion upon disposal.
        /// Purges any dangling session transition flags.
        /// </summary>
        Task<AppDbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates and opens an administrative connection without security boundary impersonation,
        /// strictly reserved for DbInitializer and migration/provisioning tasks.
        /// </summary>
        Task<SqlConnection> CreateAdminConnectionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the underlying configured connection string.
        /// </summary>
        string ConnectionString { get; }
    }
}
