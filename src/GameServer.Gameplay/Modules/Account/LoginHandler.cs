using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;

namespace GameServer.Gameplay.Modules.Account;

public sealed class LoginHandler(AccountService service) : MessageHandler<LoginRequest>((int)MessageId.Login, LoginRequest.Parser)
{
    public override bool RequiresAuthentication => false;
    protected override async ValueTask HandleMessageAsync(GameSession session, PacketHeader header, LoginRequest message, CancellationToken ct)
    {
        long id = await service.LoginAsync(session, message.DeviceId, message.AccessToken, ct);
        await session.SendAsync(header, ResultCode.Ok, new LoginResponse { PlayerId = id, DeviceId = message.DeviceId }, ct);
    }
}
