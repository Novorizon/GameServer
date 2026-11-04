namespace GameServer.Gameplay.Modules.Facebook;

public interface IFacebookBindingRepository
{
    ValueTask<bool> TryBindAsync(long playerId, string userId, CancellationToken ct);
}
