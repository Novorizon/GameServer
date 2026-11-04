namespace GameServer.Gameplay.Modules.PlayerSave;

public interface IPlayerSaveStore
{
    ValueTask<byte[]?> ReadAsync(long playerId, CancellationToken ct);
    ValueTask WriteAsync(long playerId, ReadOnlyMemory<byte> data, CancellationToken ct);
}
