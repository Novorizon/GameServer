using GameServer.Data;

namespace GameServer.Infrastructure.Persistence;

public sealed class DatabaseOptions : DatabaseConnectionOptions
{
    public bool ValidateSchemaAtStartup { get; set; } = true;
}
