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
        PlayerSaveSnapshot save = await service.ReadVersionedAsync(session, ct);
        if (save.Data == null)
            throw new RequestFailureException(ResultCode.NotFound);
        await session.SendAsync(header, ResultCode.Ok, new SaveResponse { Data = ByteString.CopyFrom(save.Data), Version = save.Version }, ct);
    }
}
