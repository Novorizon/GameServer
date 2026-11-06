using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;

namespace GameServer.Gameplay.Modules.PlayerSave;

public sealed class SaveWriteHandler(PlayerSaveService service) : MessageHandler<SaveWriteRequest>((int)MessageId.SaveWrite, SaveWriteRequest.Parser)
{
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, SaveWriteRequest message, CancellationToken ct)
    {
        long? version = await service.WriteVersionedAsync(session, message.Data.Memory,
            message.HasExpectedVersion ? message.ExpectedVersion : null, message.OperationId, ct);
        await session.SendAsync(header, ResultCode.Ok, version.HasValue ? new SaveResponse { Version = version.Value } : null, ct);
    }
}
