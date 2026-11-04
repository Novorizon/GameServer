using System.Globalization;
using System.Security.Claims;
using GameServer.Gameplay.Modules.Account;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GameServer.Infrastructure.Authentication;

public sealed class JwtLoginTokenVerifier : ILoginTokenVerifier
{
    private readonly AuthenticationOptions _options;
    private readonly JsonWebTokenHandler _handler = new() { MaximumTokenSizeInBytes = 4096, MapInboundClaims = false };
    private readonly SymmetricSecurityKey _key;
    private readonly TokenValidationParameters _validation;
    public JwtLoginTokenVerifier(AuthenticationOptions options)
    {
        _options = options;
        _key = new(options.ValidateAndGetKey());
        _validation = new()
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(10),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    }
    public async ValueTask<LoginIdentity?> VerifyAsync(string token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (token.Length is < 1 or > 4096)
            return null;
        var result = await _handler.ValidateTokenAsync(token, _validation);
        ct.ThrowIfCancellationRequested();
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt ||
            !jwt.TryGetClaim("sub", out var subject) || !jwt.TryGetClaim("device", out var device) ||
            !long.TryParse(subject.Value, NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0 ||
            !jwt.TryGetClaim("iat", out var issued) || !jwt.TryGetClaim("exp", out var expires) ||
            !long.TryParse(issued.Value, out long iat) || !long.TryParse(expires.Value, out long exp) ||
            exp <= iat || exp - iat > _options.MaxTokenLifetimeSeconds || iat > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 10)
            return null;
        return new(id, device.Value);
    }
    // Only a trusted authentication service/operator may invoke issuance. The
    // server signing key must never be distributed in a game client.
    public string Issue(long playerId, string deviceId, int lifetimeSeconds = 300)
    {
        if (playerId <= 0 || string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 255 ||
            lifetimeSeconds < 1 || lifetimeSeconds > _options.MaxTokenLifetimeSeconds)
            throw new ArgumentOutOfRangeException(nameof(playerId));
        DateTime now = DateTime.UtcNow;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(lifetimeSeconds),
            Subject = new ClaimsIdentity([new("sub", playerId.ToString(CultureInfo.InvariantCulture)), new("device", deviceId)]),
            SigningCredentials = new(_key, SecurityAlgorithms.HmacSha256)
        });
    }
}
