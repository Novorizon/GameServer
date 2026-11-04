using System.Data.Common;
using GameServer.Data.Internal;
using MySqlConnector;

namespace GameServer.Data.Providers;

public sealed class MySqlDatabase : RelationalDatabase
{
    private readonly MySqlDataSource _source;
    public MySqlDatabase(DatabaseConnectionOptions options) : base(options, DatabaseProvider.MySql)
    {
        var settings = new MySqlConnectionStringBuilder(options.ConnectionString)
        {
            Pooling = true, MaximumPoolSize = options.MaximumPoolSize,
            ConnectionTimeout = options.ConnectionTimeoutSeconds,
            DefaultCommandTimeout = options.CommandTimeoutSeconds,
            UseAffectedRows = false
        };
        if (settings.MinimumPoolSize > settings.MaximumPoolSize)
            throw new InvalidOperationException("Database minimum pool size exceeds its maximum.");
        _source = new MySqlDataSourceBuilder(settings.ConnectionString).Build();
    }
    protected override async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken ct) => await _source.OpenConnectionAsync(ct);
    protected override ValueTask DisposeProviderAsync() => _source.DisposeAsync();
    internal override DatabaseException Translate(DbException error)
    {
        int number = ((MySqlException)error).Number;
        return new(number switch
        {
            1062 => DatabaseError.UniqueConstraint,
            1213 => DatabaseError.ConcurrentWrite,
            1205 or 1040 => DatabaseError.Busy,
            -1 => DatabaseError.Timeout,
            1042 or 2002 or 2003 or 2006 or 2013 => DatabaseError.Unavailable,
            1048 or 1451 or 1452 or 3819 => DatabaseError.Constraint,
            _ => DatabaseError.Other
        }, error);
    }
}
