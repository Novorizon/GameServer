using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Gameplay.Modules.Account;
using GameServer.Infrastructure.Persistence.Tables;

namespace GameServer.Infrastructure.Authentication;

// Invoked by trusted administration code only; intentionally no TCP handler.
public sealed class TrustedAccountProvisioner(IDatabase database)
{
    public async ValueTask<AccountRecord> ProvisionAsync(long playerId, string deviceId, CancellationToken ct)
    {
        Validate(playerId, deviceId);
        var accounts = new AccountRepository(database);
        AccountRecord? existing = await FindExistingAsync(playerId, deviceId, ct);
        if (existing != null) return existing;
        try { await accounts.InsertAsync(new() { Id = playerId, DeviceId = deviceId }, ct); }
        catch (DatabaseException ex) when (ex.Error == DatabaseError.UniqueConstraint)
        {
            // Concurrent provisioning may only succeed for the same immutable pair.
            return await FindExistingAsync(playerId, deviceId, ct)
                ?? throw new InvalidOperationException("Account provisioning conflicted.");
        }
        return new(playerId, deviceId);
    }

    public async ValueTask<AccountRecord?> FindExistingAsync(long playerId, string deviceId, CancellationToken ct)
    {
        Validate(playerId, deviceId);
        var accounts = new AccountRepository(database);
        AccountRow? byId = await accounts.GetAsync(playerId, ct);
        AccountRecord? byDevice = await accounts.FindByDeviceAsync(deviceId, ct);
        if (byId == null && byDevice == null) return null;
        if (byId?.DeviceId != deviceId || byDevice?.PlayerId != playerId || byDevice.DeviceId != deviceId)
            throw new InvalidOperationException("Player ID or device is already assigned to a different account.");
        return new(playerId, deviceId);
    }

    private static void Validate(long playerId, string deviceId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 255)
            throw new ArgumentException("A device ID of 1 to 255 characters is required.", nameof(deviceId));
    }
}
