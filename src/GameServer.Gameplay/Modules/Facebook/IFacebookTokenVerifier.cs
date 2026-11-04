namespace GameServer.Gameplay.Modules.Facebook;

public interface IFacebookTokenVerifier
{
    ValueTask<string?> VerifyAsync(string token, string expectedUserId, CancellationToken ct);
}
