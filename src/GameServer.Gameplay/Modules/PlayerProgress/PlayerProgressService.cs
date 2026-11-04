using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;

namespace GameServer.Gameplay.Modules.PlayerProgress;

public sealed class PlayerProgressService(IPlayerProgressRepository repository, PlayerManager players)
{
    public ValueTask<ProgressState> ReadAsync(GameSession session, CancellationToken ct) =>
        players.Get(session).ExecuteAsync(session, token => repository.ReadAsync(session.PlayerId, token), ct);
    public ValueTask<ProgressState> WriteAsync(GameSession session, int progress, long expectedVersion, CancellationToken ct)
    {
        if (progress < 0 || expectedVersion < 0 || expectedVersion == long.MaxValue)
            throw new RequestFailureException(ResultCode.InvalidRequest);
        return players.Get(session).ExecuteAsync(session, async token =>
        {
            if (!await repository.TryWriteAsync(session.PlayerId, progress, expectedVersion, token))
                throw new RequestFailureException(ResultCode.Conflict);
            return new ProgressState(progress, expectedVersion + 1);
        }, ct);
    }
}
