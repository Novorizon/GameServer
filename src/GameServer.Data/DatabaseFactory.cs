using GameServer.Data.Abstractions;
using GameServer.Data.Providers;

namespace GameServer.Data;

public static class DatabaseFactory
{
    public static IDatabase Create(DatabaseConnectionOptions options) => options.Provider switch
    {
        DatabaseProvider.MySql => new MySqlDatabase(options),
        DatabaseProvider.Sqlite => new SqliteDatabase(options),
        _ => throw new InvalidOperationException("Unknown database provider.")
    };
}
