using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;

namespace GameServer.Gameplay.Modules.PlayerSave;

public sealed class SaveReadHandler(PlayerSaveService service) : MessageHandler<SaveReadRequest>((int)MessageId.SaveRead, SaveReadRequest.Parser)
{
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, SaveReadRequest message, CancellationToken ct)
    {
        byte[]? data = await service.ReadAsync(session, ct);
        if (data == null)
            throw new RequestFailureException(ResultCode.NotFound);
        await session.SendAsync(header, ResultCode.Ok, new SaveResponse { Data = ByteString.CopyFrom(data) }, ct);
    }
}
