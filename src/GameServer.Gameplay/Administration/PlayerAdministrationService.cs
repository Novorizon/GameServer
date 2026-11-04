using GameServer.Gameplay.Players;
using GameServer.Networking.Abstractions;
using Microsoft.Extensions.Logging;

namespace GameServer.Gameplay.Administration;

public sealed class PlayerAdministrationService(PlayerManager players, ILogger<PlayerAdministrationService> logger) : IPlayerAdministrationService
{
    public bool Kick(long playerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        if (!players.TryGetCurrentSession(playerId, out var session)) return false;
        // Close the selected session itself; never look up the player again
        // after an await and accidentally close a replacement connection.
        session.Close(DisconnectReason.AdministrativeKick);
        logger.LogInformation("Player {PlayerId} session {SessionId} disconnected by administrator", playerId, session.Id);
        return true;
    }
}
