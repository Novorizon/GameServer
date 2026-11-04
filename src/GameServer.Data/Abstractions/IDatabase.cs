using System.Data;

namespace GameServer.Data.Abstractions;

public interface IDatabase : IDatabaseExecutor, IDisposable, IAsyncDisposable
{
    ValueTask<T> WithSessionAsync<T>(Func<IDatabaseSession, CancellationToken, ValueTask<T>> action, CancellationToken ct = default);
    ValueTask<T> InTransactionAsync<T>(Func<IDatabaseSession, CancellationToken, ValueTask<T>> action,
        IsolationLevel isolation = IsolationLevel.Serializable, CancellationToken ct = default);
}
