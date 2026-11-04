using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;

namespace GameServer.Gameplay.Modules.PlayerProgress;

public sealed class ProgressReadHandler(PlayerProgressService service) : MessageHandler<ProgressReadRequest>((int)MessageId.ProgressRead, ProgressReadRequest.Parser)
{
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, ProgressReadRequest message, CancellationToken ct)
    {
        var state = await service.ReadAsync(session, ct);
        await session.SendAsync(header, ResultCode.Ok, new ProgressResponse { Progress = state.Progress, Version = state.Version }, ct);
    }
}
