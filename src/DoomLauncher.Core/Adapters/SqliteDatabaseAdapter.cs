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
            // Avoid SqliteParameter(string, SqliteType) overload resolution on int/enum values.
            if (!string.IsNullOrEmpty(name) && name[0] != '@')
                name = "@" + name;
            if (value is System.Enum)
                value = System.Convert.ToInt32(value);
            var parameter = new SqliteParameter { ParameterName = name };
            parameter.Value = value ?? System.DBNull.Value;
            return parameter;
        }
    }
}
