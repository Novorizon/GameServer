namespace GameServer.Gameplay.Modules.Account;

public interface ILoginTokenVerifier
{
    ValueTask<LoginIdentity?> VerifyAsync(string token, CancellationToken ct);
}
