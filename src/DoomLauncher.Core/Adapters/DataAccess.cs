using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

#pragma warning disable CA2100

namespace DoomLauncher
{
    public class DataAccess
    {
        public DataAccess(IDatabaseAdapter dbAdapter, string connectionString)
        {
            DbAdapter = dbAdapter;
            ConnectionString = connectionString;
        }

        public DataSet ExecuteSelect(string sql)
        {
            return ExecuteSelect(sql, Array.Empty<DbParameter>());
        }

        public DataSet ExecuteSelect(string sql, IEnumerable<DbParameter> parameters)
        {
            using DbConnection conn = DbAdapter.CreateConnection(ConnectionString);
            conn.Open();

            using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            foreach (DbParameter dbParam in parameters)
                cmd.Parameters.Add(dbParam);

            using var reader = cmd.ExecuteReader();
            DataSet ds = new DataSet();
            DataTable table = new DataTable();
            table.Load(reader);
            ds.Tables.Add(table);
            return ds;
        }

        public void ExecuteNonQuery(string sql)
        {
            ExecuteNonQuery(sql, Array.Empty<DbParameter>(), false);
        }

        public int ExecuteInsertionNonQuery(string sql, IEnumerable<DbParameter> parameters)
        {
            return ExecuteNonQuery(sql, parameters, true);
        }

        public int ExecuteInsertionNonQuery(string sql) => 
            ExecuteNonQuery(sql, Array.Empty<DbParameter>(), true);

        public void ExecuteNonQuery(string sql, IEnumerable<DbParameter> parameters) => 
            ExecuteNonQuery(sql, parameters, false);

        private int ExecuteNonQuery(string sql, IEnumerable<DbParameter> parameters, bool returnInsertedId)
        {
            using DbConnection conn = DbAdapter.CreateConnection(ConnectionString);
            conn.Open();

            using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            foreach (DbParameter dbParam in parameters)
                cmd.Parameters.Add(dbParam);

            cmd.ExecuteNonQuery();

            int insertedId = -1;
            if (returnInsertedId)
                insertedId = GetLastInsertedId(conn);

            return insertedId;
        }

        private int GetLastInsertedId(DbConnection openConnection)
        {
            using DbCommand cmd = openConnection.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid();";
            object result = cmd.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        public IDatabaseAdapter DbAdapter { get; private set; }
        public string ConnectionString { get; private set; }
    }
}
