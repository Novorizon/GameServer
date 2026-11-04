using System.Data;
using System.Data.Common;
using GameServer.Data.Abstractions;

namespace GameServer.Data.Internal;

public abstract class RelationalDatabase : IDatabase
{
    private readonly SemaphoreSlim _slots;
    private readonly int _capacity;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new();
    private int _operations;
    private bool _stopping;
    private Task? _disposeTask;
    internal int CommandTimeout { get; }
    internal int SessionCommandCapacity { get; }
    public DatabaseProvider Provider { get; }

    protected RelationalDatabase(DatabaseConnectionOptions options, DatabaseProvider provider)
    {
        options.Validate();
        Provider = provider;
        int concurrency = options.MaxConcurrentOperations > 0 ? options.MaxConcurrentOperations :
            provider == DatabaseProvider.Sqlite ? 4 : checked((int)options.MaximumPoolSize);
        _slots = new(concurrency, concurrency);
        _capacity = checked(concurrency + options.MaxQueuedOperations);
        CommandTimeout = checked((int)options.CommandTimeoutSeconds);
        SessionCommandCapacity = checked(options.MaxQueuedOperations + 1);
    }

    protected abstract ValueTask<DbConnection> OpenConnectionAsync(CancellationToken ct);
    protected abstract ValueTask DisposeProviderAsync();
    internal abstract DatabaseException Translate(DbException error);
    internal virtual ValueTask<T> RunProviderAsync<T>(DbConnection connection,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct) => action(ct);

    public ValueTask<int> ExecuteAsync(SqlStatement statement, CancellationToken ct = default) =>
        WithSessionAsync((session, token) => session.ExecuteAsync(statement, token), ct);
    public ValueTask<object?> ScalarAsync(SqlStatement statement, CancellationToken ct = default) =>
        WithSessionAsync((session, token) => session.ScalarAsync(statement, token), ct);
    public ValueTask<long> InsertIdentityAsync(SqlStatement statement, CancellationToken ct = default) =>
        WithSessionAsync((session, token) => session.InsertIdentityAsync(statement, token), ct);
    public ValueTask<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, Func<DbDataReader, T> map,
        int maxRows = 1000, CancellationToken ct = default) =>
        WithSessionAsync((session, token) => session.QueryAsync(statement, map, maxRows, token), ct);
    public ValueTask<T> WithSessionAsync<T>(Func<IDatabaseSession, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) =>
        RunAsync(action, null, ct);
    public ValueTask<T> InTransactionAsync<T>(Func<IDatabaseSession, CancellationToken, ValueTask<T>> action,
        IsolationLevel isolation = IsolationLevel.Serializable, CancellationToken ct = default) => RunAsync(action, isolation, ct);

    private async ValueTask<T> RunAsync<T>(Func<IDatabaseSession, CancellationToken, ValueTask<T>> action,
        IsolationLevel? isolation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_operations == _capacity) throw new DatabaseException(DatabaseError.Busy);
            _operations++;
        }
        bool entered = false;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
        try
        {
            await _slots.WaitAsync(operation.Token); entered = true;
            await using var connection = await OpenConnectionAsync(operation.Token);
            await using DbTransaction? transaction = isolation.HasValue ?
                await RunProviderAsync(connection, token => connection.BeginTransactionAsync(isolation.Value, token), operation.Token) : null;
            await using var session = new DatabaseSession(this, connection, transaction, operation.Token);
            try
            {
                T result = await action(session, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                await session.SealAsync();
                session.EnsureCommittable();
                if (transaction != null)
                    await RunProviderAsync(connection, async token => { await transaction.CommitAsync(token); return true; }, operation.Token);
                return result;
            }
            catch
            {
                await session.SealAsync();
                if (transaction != null)
                {
                    // Rollback is cleanup: caller cancellation must not skip it.
                    try { await RunProviderAsync(connection, async token => { await transaction.RollbackAsync(token); return true; }, CancellationToken.None); }
                    catch (DbException) { /* Connection disposal also abandons the transaction; preserve the original error. */ }
                }
                throw;
            }
        }
        catch (DbException ex) when (operation.IsCancellationRequested) { throw new OperationCanceledException("Database operation cancelled.", ex, operation.Token); }
        catch (DbException ex) { throw Translate(ex); }
        finally
        {
            if (entered) _slots.Release();
            lock (_sync) { _operations--; if (_stopping && _operations == 0) _drained.TrySetResult(); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposeTask != null) return new(_disposeTask);
            _stopping = true;
            if (_operations == 0) _drained.TrySetResult();
            _disposeTask = Task.Run(DisposeCoreAsync);
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Runs outside the owner lock and caller synchronization context.
        _shutdown.Cancel();
        await _drained.Task;
        await DisposeProviderAsync();
        _slots.Dispose(); _shutdown.Dispose();
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
