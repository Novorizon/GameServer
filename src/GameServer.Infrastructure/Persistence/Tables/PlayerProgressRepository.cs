using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Data.Tables;
using GameServer.Gameplay.Modules.PlayerProgress;

namespace GameServer.Infrastructure.Persistence.Tables;

public sealed class PlayerProgressRepository(IDatabaseExecutor database) : TableRepository<PlayerProgressRow, long>(database), IPlayerProgressRepository
{
    public async ValueTask<ProgressState> ReadAsync(long playerId, CancellationToken ct)
    {
        var row = await GetAsync(playerId, ct);
        return row == null ? new(0, 0) : new(row.Progress, row.Version);
    }

    public async ValueTask<bool> TryWriteAsync(long playerId, int progress, long expectedVersion, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        if (progress < 0 || expectedVersion < 0 || expectedVersion == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(progress));
        string sql = expectedVersion == 0
            ? $"INSERT INTO {TableName}(player_id,progress,version) VALUES(@id,@progress,1)"
            : $"UPDATE {TableName} SET progress=@progress,version=version+1 WHERE player_id=@id AND version=@version";
        try
        {
            return await Database.ExecuteAsync(new(sql, ("id", playerId), ("progress", progress), ("version", expectedVersion)), ct) == 1;
        }
        catch (DatabaseException ex) when (ex.Error is DatabaseError.UniqueConstraint or DatabaseError.ConcurrentWrite) { return false; }
    }
}
