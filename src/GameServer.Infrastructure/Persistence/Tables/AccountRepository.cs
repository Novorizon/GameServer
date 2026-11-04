using GameServer.Data.Abstractions;
using GameServer.Data.Tables;
using GameServer.Gameplay.Modules.Account;

namespace GameServer.Infrastructure.Persistence.Tables;

public sealed class AccountRepository(IDatabaseExecutor database) : TableRepository<AccountRow, long>(database), IAccountRepository
{
    public async ValueTask<AccountRecord?> FindByDeviceAsync(string deviceId, CancellationToken ct)
    {
        var rows = await FindByAsync(nameof(AccountRow.DeviceId), deviceId, limit: 1, ct: ct);
        return rows.Count == 0 ? null : new(rows[0].Id, rows[0].DeviceId);
    }
}
