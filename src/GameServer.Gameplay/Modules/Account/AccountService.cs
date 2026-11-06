using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;
using GameServer.Protocol.Framing;

namespace GameServer.Gameplay.Modules.Account;

public sealed class AccountService(IAccountRepository accounts, ILoginTokenVerifier verifier, PlayerManager players)
{
    public async ValueTask<long> LoginAsync(GameSession session, string deviceId, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 255 || token.Length is < 1 or > 4096)
            throw new RequestFailureException(ResultCode.InvalidCredentials);
        if (session.PlayerId != 0 && session.DeviceId != deviceId)
            throw new RequestFailureException(ResultCode.Conflict);
        LoginIdentity? identity = await verifier.VerifyAsync(token, ct);
        if (identity == null || identity.DeviceId != deviceId)
            throw new RequestFailureException(ResultCode.InvalidCredentials);
        AccountRecord? account = await accounts.FindByDeviceAsync(deviceId, ct);
        if (account == null || account.PlayerId != identity.PlayerId || account.DeviceId != deviceId)
            throw new RequestFailureException(ResultCode.InvalidCredentials);
        ct.ThrowIfCancellationRequested();
        players.Bind(session, new(account.PlayerId, account.DeviceId));
        return account.PlayerId;
    }
}
