namespace GameServer.Infrastructure.Authentication;

public sealed class AuthenticationOptions
{
    public string Issuer { get; set; } = "GameServer.Auth";
    public string Audience { get; set; } = "GameServer.Tcp";
    public string SigningKeyBase64 { get; set; } = "";
    public int MaxTokenLifetimeSeconds { get; set; } = 3600;
    public byte[] ValidateAndGetKey()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || MaxTokenLifetimeSeconds is < 30 or > 86400)
            throw new InvalidOperationException("Invalid Authentication configuration.");
        byte[] key;
        try
        {
            key = Convert.FromBase64String(SigningKeyBase64);
        }
        catch (FormatException) { throw new InvalidOperationException("Authentication signing key must be base64."); }
        if (key.Length < 32)
            throw new InvalidOperationException("Authentication signing key must contain at least 32 random bytes.");
        return key;
    }
}
