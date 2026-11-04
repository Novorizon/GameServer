using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Abstractions;
using GameServer.Protocol.Framing;

namespace GameServer.Gameplay.Players;

public sealed class PlayerManager(SessionManager sessions, GameplayMetrics metrics)
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<long, PlayerExecutionContext> _players = [];

    public bool TryGetCurrentSession(long playerId, [NotNullWhen(true)] out GameSession? session)
    {
        lock (_sync)
        {
            if (_players.TryGetValue(playerId, out var context) &&
                sessions.TryGet(Interlocked.Read(ref context.SessionId), out session) &&
                session is { State: SessionState.Authenticated } && session.Connection.State == ConnectionState.Open)
                return true;
            session = null;
            return false;
        }
    }

    public void Bind(GameSession session, Player player)
    {
        GameSession? old = null;
        lock (_sync)
        {
            if (!_players.TryGetValue(player.Id, out var context))
            {
                context = new(player);
                _players[player.Id] = context;
                metrics.Player(1);
            }
            sessions.TryGet(Interlocked.Read(ref context.SessionId), out old);
            Interlocked.Exchange(ref context.SessionId, session.Id);
            session.Authenticate(player.Id, player.DeviceId);
        }
        if (old != null && old.Id != session.Id)
            old.Close(DisconnectReason.Replaced);
    }
    public void Unbind(GameSession session)
    {
        lock (_sync)
        {
            if (_players.TryGetValue(session.PlayerId, out var player) && Interlocked.Read(ref player.SessionId) == session.Id)
            {
                _players.TryRemove(session.PlayerId, out _);
                metrics.Player(-1);
            }
        }
    }
    public PlayerExecutionContext Get(GameSession session)
    {
        if (!_players.TryGetValue(session.PlayerId, out var context))
            throw new RequestFailureException(ResultCode.NotAuthenticated);
        return context;
    }
}
