using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;
using GameServer.Gameplay.Dispatching;
using GameServer.Protocol.Framing;

namespace GameServer.Gameplay.Modules.PlayerSave;

public sealed class PlayerSaveService(IPlayerSaveStore store, PlayerManager players)
{
    public ValueTask<byte[]?> ReadAsync(GameSession session, CancellationToken ct) =>
        players.Get(session).ExecuteAsync(session, token => store.ReadAsync(session.PlayerId, token), ct);
    public ValueTask<PlayerSaveSnapshot> ReadVersionedAsync(GameSession session, CancellationToken ct) =>
        players.Get(session).ExecuteAsync(session, async token => store is IVersionedPlayerSaveStore versioned
            ? await versioned.ReadVersionedAsync(session.PlayerId, token)
            : new PlayerSaveSnapshot(await store.ReadAsync(session.PlayerId, token), 0), ct);

    public ValueTask<long?> WriteVersionedAsync(GameSession session, ReadOnlyMemory<byte> data,
        long? expectedVersion, string operationId, CancellationToken ct) =>
        players.Get(session).ExecuteAsync<long?>(session, async token =>
        {
            if (store is IVersionedPlayerSaveStore versioned)
            {
                if (!expectedVersion.HasValue)
                    throw new RequestFailureException(ResultCode.InvalidRequest);
                return await versioned.WriteVersionedAsync(session.PlayerId, data, expectedVersion.Value, operationId, token);
            }
            if (expectedVersion.HasValue || !string.IsNullOrEmpty(operationId))
                throw new RequestFailureException(ResultCode.InvalidRequest);
            await store.WriteAsync(session.PlayerId, data, token);
            return null;
        }, ct);
    public async ValueTask WriteAsync(GameSession session, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await players.Get(session).ExecuteAsync(session, async token =>
        {
            await store.WriteAsync(session.PlayerId, data, token);
            return true;
        }, ct);
    }
}
