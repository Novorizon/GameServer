using System.Globalization;
using GameServer.Data.Abstractions;
using GameServer.Data.Internal;

namespace GameServer.Data.Tables;

public class TableRepository<TEntity, TKey> : ITableRepository<TEntity, TKey> where TEntity : class, new() where TKey : notnull
{
    private readonly TableMap _map = TableMap.For(typeof(TEntity));
    protected IDatabaseExecutor Database { get; }
    protected string TableName => Quote(_map.Name);
    protected string SelectColumns => string.Join(",", _map.Columns.Select(c => Quote(c.Name)));
    protected string Quote(string identifier) => TableMap.Quote(identifier, Database.Provider);
    protected ValueTask<IReadOnlyList<TEntity>> QueryRowsAsync(SqlStatement statement, int maxRows = 1000, CancellationToken ct = default) =>
        Database.QueryAsync(statement, _map.Read<TEntity>, maxRows, ct);

    public TableRepository(IDatabaseExecutor database)
    {
        Database = database;
        if (_map.Key.Type != typeof(TKey)) throw new InvalidOperationException("Repository key type does not match the model key.");
    }

    public async ValueTask<TEntity?> GetAsync(TKey key, CancellationToken ct = default)
    {
        var rows = await Database.QueryAsync(new($"SELECT {SelectColumns} FROM {TableName} WHERE {Quote(_map.Key.Name)}=@key LIMIT 1", ("key", key)), _map.Read<TEntity>, 1, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<TKey> InsertAsync(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        object? key = _map.Key.Get(entity);
        bool generate = _map.Key.Identity && Equals(key, default(TKey));
        if (!generate && key == null) throw new ArgumentException("A non-generated key cannot be null.", nameof(entity));
        var columns = _map.Columns.Where(c => !c.Computed && !(generate && c == _map.Key)).ToArray();
        string sql = columns.Length == 0 ? Database.Provider == DatabaseProvider.MySql
            ? $"INSERT INTO {TableName} () VALUES ()" : $"INSERT INTO {TableName} DEFAULT VALUES"
            : $"INSERT INTO {TableName} ({string.Join(",", columns.Select(c => Quote(c.Name)))}) VALUES ({string.Join(",", columns.Select((_, i) => "@p" + i))})";
        var statement = new SqlStatement(sql,
            columns.Select((c, i) => ("p" + i, c.Get(entity))).ToArray());
        if (generate)
        {
            key = TableMap.ConvertValue(await Database.InsertIdentityAsync(statement, ct), typeof(TKey));
            _map.Key.Set(entity, key);
        }
        else await Database.ExecuteAsync(statement, ct);
        return (TKey)(key ?? throw new InvalidOperationException("A key value is required."));
    }

    public async ValueTask<bool> UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var columns = _map.Columns.Where(c => c != _map.Key && !c.Computed && !c.Identity).ToArray();
        if (columns.Length == 0) throw new InvalidOperationException("No updateable properties.");
        var parameters = columns.Select((c, i) => ("p" + i, c.Get(entity))).Append(("key", _map.Key.Get(entity))).ToArray();
        return await Database.ExecuteAsync(new($"UPDATE {TableName} SET {string.Join(",", columns.Select((c, i) => Quote(c.Name) + "=@p" + i))} WHERE {Quote(_map.Key.Name)}=@key", parameters), ct) == 1;
    }

    public async ValueTask<bool> DeleteAsync(TKey key, CancellationToken ct = default) =>
        await Database.ExecuteAsync(new($"DELETE FROM {TableName} WHERE {Quote(_map.Key.Name)}=@key", ("key", key)), ct) == 1;

    public ValueTask<IReadOnlyList<TEntity>> ListAsync(int limit = 100, int offset = 0, CancellationToken ct = default) =>
        SelectAsync(null, null, limit, offset, ct);
    public ValueTask<IReadOnlyList<TEntity>> FindByAsync(string property, object? value, int limit = 100,
        int offset = 0, CancellationToken ct = default) => SelectAsync(_map.Resolve(property), value, limit, offset, ct);

    private ValueTask<IReadOnlyList<TEntity>> SelectAsync(TableMap.Column? column, object? value, int limit, int offset, CancellationToken ct)
    {
        if (limit is < 1 or > 1000 || offset < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        string where = column == null ? "" : " WHERE " + Quote(column.Name) + (value == null ? " IS NULL" : "=@value");
        var parameters = new List<(string, object?)> { ("limit", limit), ("offset", offset) };
        if (column != null && value != null) parameters.Add(("value", value));
        return Database.QueryAsync(new($"SELECT {SelectColumns} FROM {TableName}{where} ORDER BY {Quote(_map.Key.Name)} LIMIT @limit OFFSET @offset", parameters.ToArray()), _map.Read<TEntity>, limit, ct);
    }

    public async ValueTask<long> CountAsync(CancellationToken ct = default) =>
        Convert.ToInt64(await Database.ScalarAsync(new($"SELECT COUNT(*) FROM {TableName}"), ct), CultureInfo.InvariantCulture);
}
