using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;

namespace GameServer.Gameplay.Modules.Facebook;

public sealed class FacebookBindHandler(FacebookBindingService service) : MessageHandler<FacebookBindRequest>((int)MessageId.FacebookBind, FacebookBindRequest.Parser)
{
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, FacebookBindRequest message, CancellationToken ct)
    {
        string userId = await service.BindAsync(session, message.AccessToken, message.ExpectedUserId, ct);
        await session.SendAsync(header, ResultCode.Ok, new FacebookBindResponse { UserId = userId }, ct);
    }
}
