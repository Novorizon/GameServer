using System.Data.Common;
using GameServer.Data.Internal;
using Microsoft.Data.Sqlite;

namespace GameServer.Data.Providers;

public sealed class SqliteDatabase : RelationalDatabase
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _memoryAnchor;
    public SqliteDatabase(DatabaseConnectionOptions options) : base(options, DatabaseProvider.Sqlite)
    {
        var settings = new SqliteConnectionStringBuilder(options.ConnectionString)
        { DefaultTimeout = checked((int)options.CommandTimeoutSeconds), Pooling = false, ForeignKeys = true };
        if (settings.DataSource == ":memory:")
        { settings.DataSource = "gameserver-" + Guid.NewGuid().ToString("N"); settings.Mode = SqliteOpenMode.Memory; }
        if (string.IsNullOrEmpty(settings.DataSource))
        {
            if (settings.Mode != SqliteOpenMode.Memory) throw new InvalidOperationException("SQLite requires a file path or :memory: data source.");
            settings.DataSource = "gameserver-" + Guid.NewGuid().ToString("N");
        }
        if (settings.Mode == SqliteOpenMode.Memory)
        {
            settings.Cache = SqliteCacheMode.Shared;
            _connectionString = settings.ConnectionString;
            _memoryAnchor = new(_connectionString); _memoryAnchor.Open();
        }
        else
        {
            _connectionString = settings.ConnectionString;
            if (settings.Mode == SqliteOpenMode.ReadWriteCreate && !string.IsNullOrEmpty(settings.DataSource))
            {
                string? parent = Path.GetDirectoryName(Path.GetFullPath(settings.DataSource));
                if (parent != null) Directory.CreateDirectory(parent);
            }
        }
    }

    protected override async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await Task.Run(() => { ct.ThrowIfCancellationRequested(); connection.Open(); }, CancellationToken.None);
            ct.ThrowIfCancellationRequested(); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    internal override async ValueTask<T> RunProviderAsync<T>(DbConnection connection,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        // Microsoft.Data.Sqlite's async calls execute synchronous native I/O.
        // Only admitted operations reach this bounded offload path.
        using var interrupt = ct.Register(() => SQLitePCL.raw.sqlite3_interrupt(((SqliteConnection)connection).Handle));
        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            T value = await action(ct);
            ct.ThrowIfCancellationRequested(); return value;
        }, CancellationToken.None);
    }

    protected override ValueTask DisposeProviderAsync() => _memoryAnchor?.DisposeAsync() ?? ValueTask.CompletedTask;
    internal override DatabaseException Translate(DbException error)
    {
        var sqlite = (SqliteException)error;
        return new(sqlite.SqliteExtendedErrorCode is 1555 or 2067 ? DatabaseError.UniqueConstraint : sqlite.SqliteErrorCode switch
        {
            5 or 6 => DatabaseError.Busy,
            19 => DatabaseError.Constraint,
            14 or 13 => DatabaseError.Unavailable,
            _ => DatabaseError.Other
        }, error);
    }
}
