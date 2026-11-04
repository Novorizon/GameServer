using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;

namespace GameServer.LoadTests;

// Registered only by this test executable's --serve entry point. Production
// GameServer.Host never registers this message ID. Authentication remains required.
public sealed class LoadEchoHandler() : MessageHandler<SaveWriteRequest>(MessageId, SaveWriteRequest.Parser)
{
    public const int MessageId = 1900000001;
    protected override ValueTask HandleMessageAsync(GameSession session, PacketHeader header, SaveWriteRequest message, CancellationToken ct)
        => session.SendAsync(header, ResultCode.Ok, new SaveResponse { Data = message.Data }, ct);
}
