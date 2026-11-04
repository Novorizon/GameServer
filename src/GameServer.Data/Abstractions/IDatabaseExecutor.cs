using System.Data.Common;

namespace GameServer.Data.Abstractions;

public interface IDatabaseExecutor
{
    DatabaseProvider Provider { get; }
    ValueTask<int> ExecuteAsync(SqlStatement statement, CancellationToken ct = default);
    ValueTask<object?> ScalarAsync(SqlStatement statement, CancellationToken ct = default);
    ValueTask<long> InsertIdentityAsync(SqlStatement statement, CancellationToken ct = default);
    ValueTask<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, Func<DbDataReader, T> map,
        int maxRows = 1000, CancellationToken ct = default);
}
