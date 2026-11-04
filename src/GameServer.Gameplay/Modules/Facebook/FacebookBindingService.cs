using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;

namespace GameServer.Gameplay.Modules.Facebook;

public sealed class FacebookBindingService(IFacebookTokenVerifier verifier, IFacebookBindingRepository repository, PlayerManager players)
{
    public async ValueTask<string> BindAsync(GameSession session, string token, string expectedUserId, CancellationToken ct)
    {
        if (token.Length is < 1 or > 4096 || expectedUserId.Length is < 1 or > 128)
            throw new RequestFailureException(ResultCode.InvalidRequest);
        string? userId = await verifier.VerifyAsync(token, expectedUserId, ct);
        if (userId == null)
            throw new RequestFailureException(ResultCode.InvalidCredentials);
        return await players.Get(session).ExecuteAsync(session, async operation =>
        {
            if (!await repository.TryBindAsync(session.PlayerId, userId, operation))
                throw new RequestFailureException(ResultCode.Conflict);
            return userId;
        }, ct);
    }
}
