using Database;
using Mono.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DataBase
{
    public enum AccessType
    {
        Immediately = 1,
        Cached = 1 << 2,

    }

    public abstract class TableAccess
    {
        public string Name { get; set; }
        public AccessType Type;
        public bool Loaded { get; set; }

        public abstract Type DataType { get; }

        public abstract bool Load(SQLiteHelper db);

        public virtual TableData GetData(int index)
        {
            return null;
        }

        public virtual TableData GetData(string key)
        {
            return null;
        }

        protected int GetInt32(SqliteDataReader reader, string field)
        {
            if (reader.IsDBNull(reader.GetOrdinal(field)))
                return 0;

            return reader.GetInt32(reader.GetOrdinal(field));
        }

        protected long GetInt64(SqliteDataReader reader, string field)
        {
            if (reader.IsDBNull(reader.GetOrdinal(field)))
                return 0;

            return reader.GetInt64(reader.GetOrdinal(field));
        }

        protected float GetFloat(SqliteDataReader reader, string field)
        {
            if (reader.IsDBNull(reader.GetOrdinal(field)))
                return 0;

            return reader.GetFloat(reader.GetOrdinal(field));
        }

        protected string GetString(SqliteDataReader reader, string field)
        {
            if (reader.IsDBNull(reader.GetOrdinal(field)))
                return null;

            return reader.GetString(reader.GetOrdinal(field));
        }
    }
}