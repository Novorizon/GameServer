namespace GameServer.Gameplay.Administration;

// A capability for trusted host/admin code, not a client protocol endpoint.
public interface IPlayerAdministrationService
{
    bool Kick(long playerId);
}
