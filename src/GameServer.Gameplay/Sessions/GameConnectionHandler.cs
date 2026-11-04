using GameServer.Gameplay.Configuration;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Players;
using GameServer.Networking.Abstractions;
using Microsoft.Extensions.Logging;

namespace GameServer.Gameplay.Sessions;

public sealed class GameConnectionHandler(GameplayOptions options, IConnectionMessageSender sender,
    SessionManager sessions, PlayerManager players, PacketDispatcher dispatcher, ILogger<GameConnectionHandler> logger) : IConnectionHandler
{
    private readonly CancellationTokenSource _stopReading = new();
    private int _unauthenticated, _draining;
    public int UnauthenticatedCount => Volatile.Read(ref _unauthenticated);
    public void BeginDrain()
    {
        Interlocked.Exchange(ref _draining, 1);
        _stopReading.Cancel();
    }

    public async Task HandleAsync(IConnection connection, CancellationToken forceStop)
    {
        if (Interlocked.Increment(ref _unauthenticated) > options.MaxUnauthenticatedConnections)
        {
            Interlocked.Decrement(ref _unauthenticated);
            connection.Abort(DisconnectReason.Overloaded);
            return;
        }
        bool reserved = true;
        var session = new GameSession(connection, sender);
        DisconnectReason reason = DisconnectReason.RemoteClosed;
        try
        {
            sessions.Add(session);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(forceStop, connection.Closed);
            while (Volatile.Read(ref _draining) == 0 && connection.State == ConnectionState.Open)
            {
                TimeSpan remaining = session.Remaining(options);
                if (remaining <= TimeSpan.Zero)
                {
                    reason = DisconnectReason.Timeout;
                    break;
                }
                using var read = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, _stopReading.Token);
                read.CancelAfter(remaining);
                using ReceivedPacket? packet = await connection.ReceiveAsync(read.Token);
                if (packet == null)
                    break;
                remaining = session.Remaining(options);
                if (remaining <= TimeSpan.Zero)
                {
                    reason = DisconnectReason.Timeout;
                    break;
                }
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                operation.CancelAfter(remaining);
                bool success = await dispatcher.DispatchAsync(session, packet, operation.Token);
                if (success && session.State == SessionState.Authenticated)
                    session.Touch();
                if (reserved && session.PlayerId != 0)
                {
                    Interlocked.Decrement(ref _unauthenticated);
                    reserved = false;
                }
            }
        }
        catch (OperationCanceledException) { reason = Volatile.Read(ref _draining) != 0 ? DisconnectReason.ServerStopping : DisconnectReason.Timeout; }
        catch (ProtocolException) { reason = DisconnectReason.ProtocolError; }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            reason = DisconnectReason.RemoteClosed;
        }
        catch (Exception ex)
        {
            logger.LogError("Session {SessionId} failed ({ExceptionType})", session.Id, ex.GetType().Name);
            reason = DisconnectReason.ApplicationError;
        }
        finally
        {
            players.Unbind(session);
            sessions.Remove(session);
            if (reserved)
                Interlocked.Decrement(ref _unauthenticated);
            session.Close(reason);
        }
    }
}
