using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;

namespace GameServer.Gameplay.Modules.PlayerSave;

public sealed class PlayerSaveService(IPlayerSaveStore store, PlayerManager players)
{
    public ValueTask<byte[]?> ReadAsync(GameSession session, CancellationToken ct) =>
        players.Get(session).ExecuteAsync(session, token => store.ReadAsync(session.PlayerId, token), ct);
    public async ValueTask WriteAsync(GameSession session, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await players.Get(session).ExecuteAsync(session, async token =>
        {
            await store.WriteAsync(session.PlayerId, data, token);
            return true;
        }, ct);
    }
}
