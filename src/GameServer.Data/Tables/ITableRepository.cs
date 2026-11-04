namespace GameServer.Data.Tables;

public interface ITableRepository<TEntity, TKey> where TEntity : class, new() where TKey : notnull
{
    ValueTask<TEntity?> GetAsync(TKey key, CancellationToken ct = default);
    ValueTask<TKey> InsertAsync(TEntity entity, CancellationToken ct = default);
    ValueTask<bool> UpdateAsync(TEntity entity, CancellationToken ct = default);
    ValueTask<bool> DeleteAsync(TKey key, CancellationToken ct = default);
    ValueTask<IReadOnlyList<TEntity>> ListAsync(int limit = 100, int offset = 0, CancellationToken ct = default);
    ValueTask<IReadOnlyList<TEntity>> FindByAsync(string property, object? value, int limit = 100,
        int offset = 0, CancellationToken ct = default);
    ValueTask<long> CountAsync(CancellationToken ct = default);
}
