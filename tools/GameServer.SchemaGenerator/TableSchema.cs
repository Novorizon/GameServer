namespace GameServer.SchemaGenerator;

public sealed record ColumnSchema(string Name, string StoreType, bool Nullable, bool PrimaryKey,
    bool Identity = false, bool Computed = false, string? DefaultSql = null);

public sealed record TableSchema(string Name, IReadOnlyList<ColumnSchema> Columns);

public sealed class SchemaGenerationException(string message) : Exception(message);
