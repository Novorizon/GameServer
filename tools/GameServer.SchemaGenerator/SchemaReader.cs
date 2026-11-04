using GameServer.Data;
using GameServer.Data.Abstractions;

namespace GameServer.SchemaGenerator;

/// <summary>Reads metadata only. Tables must be explicitly selected; views and virtual tables are excluded.</summary>
public static class SchemaReader
{
    public static async Task<IReadOnlyList<TableSchema>> ReadAsync(IDatabase database,
        IReadOnlyList<string> tableNames, CancellationToken ct = default)
    {
        if (tableNames.Count is < 1 or > 256 || tableNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tableNames.Count)
            throw new SchemaGenerationException("Select between 1 and 256 distinct tables.");
        foreach (string table in tableNames) CodeGenerator.ValidateSqlIdentifier(table);
        var result = new List<TableSchema>();
        foreach (string table in tableNames)
            result.Add(database.Provider == DatabaseProvider.MySql
                ? await ReadMySqlAsync(database, table, ct) : await ReadSqliteAsync(database, table, ct));
        return result;
    }

    private static async Task<TableSchema> ReadMySqlAsync(IDatabase database, string table, CancellationToken ct)
    {
        var tables = await database.QueryAsync(new("""
            SELECT TABLE_NAME FROM information_schema.TABLES
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND TABLE_TYPE='BASE TABLE'
            """, ("table", table)), r => r.GetString(0), 1, ct);
        if (tables.Count != 1 || !string.Equals(tables[0], table, StringComparison.Ordinal))
            throw new SchemaGenerationException($"Table '{table}' was not found with the exact name in the selected database.");
        var columns = await database.QueryAsync(new("""
            SELECT COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE,COLUMN_KEY,EXTRA,COLUMN_DEFAULT
            FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table
            ORDER BY ORDINAL_POSITION
            """, ("table", table)), r => new ColumnSchema(r.GetString(0), r.GetString(1), r.GetString(2) == "YES",
                r.GetString(3) == "PRI", r.GetString(4).Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                r.GetString(4).Contains("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase) ||
                r.GetString(4).Contains("STORED GENERATED", StringComparison.OrdinalIgnoreCase),
                r.IsDBNull(5) ? null : r.GetString(5)), 4096, ct);
        return new(table, columns);
    }

    private static async Task<TableSchema> ReadSqliteAsync(IDatabase database, string table, CancellationToken ct)
    {
        var tables = await database.QueryAsync(new("""
            SELECT name,type,wr FROM pragma_table_list WHERE schema='main' AND name=@table
            """, ("table", table)), r => (Name: r.GetString(0), Type: r.GetString(1), WithoutRowId: r.GetInt64(2) != 0), 1, ct);
        if (tables.Count != 1 || tables[0].Name != table || tables[0].Type != "table")
            throw new SchemaGenerationException($"'{table}' must be an ordinary table in SQLite's main database.");
        var columns = await database.QueryAsync(new("""
            SELECT name,type,"notnull",pk,hidden,dflt_value FROM pragma_table_xinfo(@table,'main') ORDER BY cid
            """, ("table", table)), r => new ColumnSchema(r.GetString(0), r.GetString(1), r.GetInt64(2) == 0,
                r.GetInt64(3) > 0, Computed: r.GetInt64(4) is 2 or 3,
                DefaultSql: r.IsDBNull(5) ? null : r.GetString(5)), 4096, ct);
        // A true INTEGER PRIMARY KEY aliases rowid and has no separate primary-key index.
        // Checking the index also handles the historical 'INTEGER PRIMARY KEY DESC' exception.
        var pkIndexes = await database.QueryAsync(new("""
            SELECT name FROM pragma_index_list(@table,'main') WHERE origin='pk'
            """, ("table", table)), r => r.GetString(0), 4096, ct);
        bool rowId = !tables[0].WithoutRowId && pkIndexes.Count == 0 && columns.Count(c => c.PrimaryKey) == 1;
        return new(table, columns.Select(c => c with
        {
            Identity = rowId && c.PrimaryKey && c.StoreType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase),
            Nullable = c.Nullable && !(tables[0].WithoutRowId && c.PrimaryKey) &&
                !(rowId && c.PrimaryKey && c.StoreType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
        }).ToArray());
    }
}
