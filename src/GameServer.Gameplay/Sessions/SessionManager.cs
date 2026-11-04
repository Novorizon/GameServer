using System.Collections.Concurrent;
using GameServer.Gameplay.Diagnostics;

namespace GameServer.Gameplay.Sessions;

public sealed class SessionManager(GameplayMetrics metrics)
{
    private readonly ConcurrentDictionary<long, GameSession> _sessions = [];
    public bool TryGet(long id, out GameSession? session) => _sessions.TryGetValue(id, out session);
    public void Add(GameSession session)
    {
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException("Duplicate session ID.");
        metrics.Session(1);
    }
    public void Remove(GameSession session)
    {
        if (((ICollection<KeyValuePair<long, GameSession>>)_sessions).Remove(new(session.Id, session)))
            metrics.Session(-1);
    }
}
