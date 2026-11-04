using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;

namespace GameServer.Gameplay.Modules.PlayerProgress;

public sealed class ProgressWriteHandler(PlayerProgressService service) : MessageHandler<ProgressWriteRequest>((int)MessageId.ProgressWrite, ProgressWriteRequest.Parser)
{
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, ProgressWriteRequest message, CancellationToken ct)
    {
        var state = await service.WriteAsync(session, message.Progress, message.ExpectedVersion, ct);
        await session.SendAsync(header, ResultCode.Ok, new ProgressResponse { Progress = state.Progress, Version = state.Version }, ct);
    }
}
