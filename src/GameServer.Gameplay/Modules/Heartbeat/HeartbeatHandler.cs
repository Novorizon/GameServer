using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;

namespace GameServer.Gameplay.Modules.Heartbeat;

public sealed class HeartbeatHandler : IMessageHandler
{
    public int Id => (int)MessageId.Heartbeat;
    public bool RequiresAuthentication => true;
    public ValueTask HandleAsync(GameSession session, PacketHeader header, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (!payload.IsEmpty)
            throw new RequestFailureException(ResultCode.InvalidRequest);
        return session.SendAsync(header, ResultCode.Ok, null, ct);
    }
}
