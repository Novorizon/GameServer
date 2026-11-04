using System.Globalization;
using GameServer.Data;
using GameServer.Data.Abstractions;
using GameServer.Data.Tables;
using GameServer.Gameplay.Modules.Facebook;

namespace GameServer.Infrastructure.Persistence.Tables;

public sealed class FacebookBindingRepository(IDatabaseExecutor database) : TableRepository<FacebookBindingRow, long>(database), IFacebookBindingRepository
{
    public async ValueTask<bool> TryBindAsync(long playerId, string userId, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        try
        {
            await Database.ExecuteAsync(new($"INSERT INTO {TableName}(player_id,user_id) VALUES(@id,@user)", ("id", playerId), ("user", userId)), ct);
            return true;
        }
        catch (DatabaseException ex) when (ex.Error == DatabaseError.UniqueConstraint)
        {
            return Convert.ToInt64(await Database.ScalarAsync(new($"SELECT COUNT(*) FROM {TableName} WHERE player_id=@id AND user_id=@user",
                ("id", playerId), ("user", userId)), ct), CultureInfo.InvariantCulture) == 1;
        }
    }
}
