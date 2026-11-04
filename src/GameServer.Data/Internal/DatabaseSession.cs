using System.Data.Common;
using System.Globalization;
using GameServer.Data.Abstractions;

namespace GameServer.Data.Internal;

internal sealed class DatabaseSession(RelationalDatabase database, DbConnection connection,
    DbTransaction? transaction, CancellationToken lifetime) : IDatabaseSession, IAsyncDisposable
{
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly CancellationTokenSource _scope = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    private int _closed;
    private int _pending;
    private Exception? _transactionFailure;
    public DatabaseProvider Provider => database.Provider;

    private DbCommand Command(SqlStatement statement)
    {
        var command = connection.CreateCommand();
        try
        {
            command.Transaction = transaction;
            command.CommandTimeout = database.CommandTimeout;
            command.CommandText = statement.Text;
            foreach (var (name, value) in statement.Parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@" + name;
                parameter.Value = value is Enum enumeration
                    ? Convert.ChangeType(enumeration, Enum.GetUnderlyingType(enumeration.GetType()), CultureInfo.InvariantCulture)
                    : value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            return command;
        }
        catch { command.Dispose(); throw; }
    }

    private async ValueTask<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        if (Interlocked.Increment(ref _pending) > database.SessionCommandCapacity)
        { Interlocked.Decrement(ref _pending); throw new DatabaseException(DatabaseError.Busy); }
        bool entered = false;
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, _scope.Token);
            await _commands.WaitAsync(operation.Token); entered = true;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
            EnsureCommittable();
            try
            {
                try
                {
                    T value = await database.RunProviderAsync(connection, action, operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    return value;
                }
                catch (DbException ex) when (operation.IsCancellationRequested) { throw new OperationCanceledException("Database command cancelled.", ex, operation.Token); }
                catch (DbException ex) { throw database.Translate(ex); }
            }
            catch (Exception ex)
            {
                if (transaction != null) Interlocked.CompareExchange(ref _transactionFailure, ex, null);
                throw;
            }
        }
        finally { if (entered) _commands.Release(); Interlocked.Decrement(ref _pending); }
    }

    public ValueTask<int> ExecuteAsync(SqlStatement statement, CancellationToken ct = default) => RunAsync(async token =>
    { await using var command = Command(statement); return await command.ExecuteNonQueryAsync(token); }, ct);

    public ValueTask<object?> ScalarAsync(SqlStatement statement, CancellationToken ct = default) => RunAsync(async token =>
    {
        await using var command = Command(statement);
        object? value = await command.ExecuteScalarAsync(token);
        return value is DBNull ? null : value;
    }, ct);

    public ValueTask<long> InsertIdentityAsync(SqlStatement statement, CancellationToken ct = default) => RunAsync(async token =>
    {
        await using var insert = Command(statement);
        if (await insert.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("Identity insert must affect one row.");
        await using var identity = connection.CreateCommand(); identity.Transaction = transaction;
        identity.CommandTimeout = database.CommandTimeout;
        identity.CommandText = Provider == DatabaseProvider.MySql ? "SELECT LAST_INSERT_ID()" : "SELECT last_insert_rowid()";
        return Convert.ToInt64(await identity.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }, ct);

    public ValueTask<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, Func<DbDataReader, T> map,
        int maxRows = 1000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (maxRows is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        return RunAsync<IReadOnlyList<T>>(async token =>
        {
            await using var command = Command(statement);
            await using var reader = await command.ExecuteReaderAsync(token);
            var rows = new List<T>();
            while (await reader.ReadAsync(token))
            {
                token.ThrowIfCancellationRequested();
                if (rows.Count == maxRows) throw new InvalidOperationException("Query exceeded its row limit; use pagination.");
                rows.Add(map(reader));
            }
            return rows;
        }, ct);
    }

    internal async ValueTask SealAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _scope.Cancel();
        await _commands.WaitAsync();
        _commands.Release();
    }
    internal void EnsureCommittable()
    {
        var failure = Volatile.Read(ref _transactionFailure);
        if (failure != null) throw new DatabaseException(DatabaseError.TransactionAborted, failure);
    }
    public async ValueTask DisposeAsync()
    {
        await SealAsync();
        _scope.Dispose(); _commands.Dispose();
    }
}
