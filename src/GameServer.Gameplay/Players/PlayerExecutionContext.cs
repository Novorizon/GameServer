using System.Collections.Concurrent;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Abstractions;
using GameServer.Protocol.Framing;

namespace GameServer.Gameplay.Players;

public sealed class PlayerExecutionContext(Player player)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pending;
    internal long SessionId;
    public Player Player { get; } = player;
    public async ValueTask<T> ExecuteAsync<T>(GameSession session, Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _pending) > 4)
        {
            Interlocked.Decrement(ref _pending);
            throw new RequestFailureException(ResultCode.ServerBusy);
        }
        bool locked = false;
        try
        {
            await _gate.WaitAsync(ct);
            locked = true;
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Read(ref SessionId) != session.Id)
                throw new RequestFailureException(ResultCode.SessionReplaced);
            return await action(ct);
        }
        finally { if (locked) _gate.Release(); Interlocked.Decrement(ref _pending); }
    }
}
