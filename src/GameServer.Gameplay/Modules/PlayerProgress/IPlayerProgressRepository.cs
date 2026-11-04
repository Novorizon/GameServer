namespace GameServer.Gameplay.Modules.PlayerProgress;

public interface IPlayerProgressRepository
{
    ValueTask<ProgressState> ReadAsync(long playerId, CancellationToken ct);
    ValueTask<bool> TryWriteAsync(long playerId, int progress, long expectedVersion, CancellationToken ct);
}
