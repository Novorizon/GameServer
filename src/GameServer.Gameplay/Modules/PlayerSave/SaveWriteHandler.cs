using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;

namespace GameServer.Gameplay.Modules.PlayerSave;

public sealed class SaveWriteHandler(PlayerSaveService service) : MessageHandler<SaveWriteRequest>((int)MessageId.SaveWrite, SaveWriteRequest.Parser)
{
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, SaveWriteRequest message, CancellationToken ct)
    {
        await service.WriteAsync(session, message.Data.Memory, ct);
        await session.SendAsync(header, ResultCode.Ok, null, ct);
    }
}
