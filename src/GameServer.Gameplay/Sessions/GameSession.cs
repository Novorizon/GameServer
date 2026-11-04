using System.Diagnostics;
using GameServer.Gameplay.Configuration;
using GameServer.Networking.Abstractions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;

namespace GameServer.Gameplay.Sessions;

public enum SessionState
{
    AwaitingLogin, Authenticated, Closed
}

public sealed class GameSession(IConnection connection, IConnectionMessageSender sender)
{
    private long _playerId;
    private int _state;
    private readonly long _created = Stopwatch.GetTimestamp();
    private long _lastActivity = Stopwatch.GetTimestamp();
    public long Id => Connection.Id;
    public IConnection Connection { get; } = connection;
    public SessionState State => (SessionState)Volatile.Read(ref _state);
    public long PlayerId => Interlocked.Read(ref _playerId);
    public string? DeviceId
    {
        get; private set;
    }
    internal void Authenticate(long playerId, string deviceId)
    {
        DeviceId = deviceId;
        Interlocked.Exchange(ref _playerId, playerId);
        Volatile.Write(ref _state, (int)SessionState.Authenticated);
    }
    public void Touch() => Interlocked.Exchange(ref _lastActivity, Stopwatch.GetTimestamp());
    public TimeSpan Remaining(GameplayOptions options)
    {
        bool authenticated = State == SessionState.Authenticated;
        return TimeSpan.FromSeconds(authenticated ? options.IdleTimeoutSeconds : options.LoginTimeoutSeconds) -
            Stopwatch.GetElapsedTime(authenticated ? Interlocked.Read(ref _lastActivity) : _created);
    }
    public void Close(DisconnectReason reason)
    {
        Interlocked.Exchange(ref _state, (int)SessionState.Closed);
        Connection.Abort(reason);
    }
    public ValueTask SendAsync(PacketHeader request, ResultCode result, IMessage? message, CancellationToken ct) =>
        sender.SendAsync(Connection, request.MessageId, request.RequestId, result, message, ct);

    public ValueTask SendNotificationAsync(MessageId messageId, IMessage message, CancellationToken ct)
    {
        if (!PacketCodec.IsNotificationMessageId((int)messageId))
            throw new ArgumentOutOfRangeException(nameof(messageId));
        ArgumentNullException.ThrowIfNull(message);
        return sender.SendAsync(Connection, (int)messageId, PacketCodec.NotificationRequestId, ResultCode.Ok, message, ct);
    }
}
