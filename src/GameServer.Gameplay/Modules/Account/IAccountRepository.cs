namespace GameServer.Gameplay.Modules.Account;

public interface IAccountRepository
{
    ValueTask<AccountRecord?> FindByDeviceAsync(string deviceId, CancellationToken ct);
}
