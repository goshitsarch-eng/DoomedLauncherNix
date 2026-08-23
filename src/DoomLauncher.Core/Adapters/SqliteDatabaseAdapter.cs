using DoomLauncher.Interfaces;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace DoomLauncher
{
    public class SqliteDatabaseAdapter : IDatabaseAdapter
    {
        public DbConnection CreateConnection(string connectionString)
        {
            return new SqliteConnection(connectionString);
        }

        public DbParameter CreateParameter(string name, object value)
        {
            return new SqliteParameter(name, value ?? System.DBNull.Value);
        }
    }
}
