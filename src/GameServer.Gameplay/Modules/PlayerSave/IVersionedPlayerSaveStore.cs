namespace GameServer.Gameplay.Modules.PlayerSave;

// This storage is for opaque client data, never authoritative game economy state.
public sealed record PlayerSaveSnapshot(byte[]? Data, long Version);

public interface IVersionedPlayerSaveStore : IPlayerSaveStore
{
    ValueTask<PlayerSaveSnapshot> ReadVersionedAsync(long playerId, CancellationToken ct);
    ValueTask<long> WriteVersionedAsync(long playerId, ReadOnlyMemory<byte> data,
        long expectedVersion, string operationId, CancellationToken ct);
}
