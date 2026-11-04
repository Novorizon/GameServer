using System.Net;
using System.Text;
using GameServer.Infrastructure.Authentication;
using GameServer.Infrastructure.Storage;
using GameServer.Infrastructure.External.Facebook;
using GameServer.Gameplay.Dispatching;
using Xunit;

namespace GameServer.UnitTests;

public sealed class SecurityAndStorageTests
{
    private static AuthenticationOptions Options() => new() { SigningKeyBase64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };
    [Fact]
    public async Task JwtRequiresSignatureIssuerAudienceAndTrustedIdentity()
    {
        var options = Options(); var verifier = new JwtLoginTokenVerifier(options);
        string token = verifier.Issue(123, "device");
        var identity = await verifier.VerifyAsync(token, default);
        Assert.Equal(123, identity!.PlayerId); Assert.Equal("device", identity.DeviceId);
        Assert.Null(await new JwtLoginTokenVerifier(Options()).VerifyAsync(token, default));
        Assert.Null(await verifier.VerifyAsync(token + "bad", default));
        Assert.Null(await verifier.VerifyAsync("", default));
        Assert.Null(await verifier.VerifyAsync("eyJhbGciOiJub25lIn0.eyJzdWIiOiIxMjMifQ.", default));
        Assert.Throws<InvalidOperationException>(() => new JwtLoginTokenVerifier(new AuthenticationOptions()));
        var wrongIssuer = Options(); wrongIssuer.SigningKeyBase64 = options.SigningKeyBase64; wrongIssuer.Issuer = "other";
        Assert.Null(await new JwtLoginTokenVerifier(wrongIssuer).VerifyAsync(token, default));
        var wrongAudience = Options(); wrongAudience.SigningKeyBase64 = options.SigningKeyBase64; wrongAudience.Audience = "other";
        Assert.Null(await new JwtLoginTokenVerifier(wrongAudience).VerifyAsync(token, default));
    }

    [Fact]
    public async Task SaveRoundtripDetectsCorruptionBoundsSizeAndCleansTemporaryFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "gameserver-save-" + Guid.NewGuid());
        try
        {
            var store = new FilePlayerSaveStore(new() { RootPath = root, MaxSaveBytes = 32 });
            Assert.Null(await store.ReadAsync(42, default));
            await store.WriteAsync(42, new byte[] { 1, 2, 3 }, default);
            Assert.Equal(new byte[] { 1, 2, 3 }, await store.ReadAsync(42, default));
            await Assert.ThrowsAsync<RequestFailureException>(() => store.WriteAsync(42, new byte[33], default).AsTask());
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync(42, new byte[] { 4 }, canceled.Token).AsTask());
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.Equal(new byte[] { 1, 2, 3 }, await store.ReadAsync(42, default));
            string path = Directory.GetFiles(root, "*.dat", SearchOption.AllDirectories).Single();
            byte[] bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes);
            await Assert.ThrowsAsync<RequestFailureException>(() => store.ReadAsync(42, default).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ReadAsync(-1, default).AsTask());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("app", "user", true, 3600, true)]
    [InlineData("wrong", "user", true, 3600, false)]
    [InlineData("app", "other", true, 3600, false)]
    [InlineData("app", "user", false, 3600, false)]
    [InlineData("app", "user", true, -1, false)]
    public async Task FacebookChecksAppUserValidityAndExpiration(string app, string user, bool valid, int seconds, bool accepted)
    {
        long exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + seconds;
        string json = System.Text.Json.JsonSerializer.Serialize(new { data = new { app_id = app, user_id = user, is_valid = valid, expires_at = exp } });
        var handler = new ResponseHandler(json); var verifier = new FacebookTokenVerifier(new ClientFactory(handler), new() { Enabled = true, AppId = "app", AppSecret = "test-only" });
        Assert.Equal(accepted ? "user" : null, await verifier.VerifyAsync("test-token", "user", default));
        Assert.Equal("graph.facebook.com", handler.RequestHost);
    }

    [Fact]
    public async Task DisabledFacebookDoesNotMakeExternalRequest()
    {
        var handler = new ResponseHandler("{}"); var verifier = new FacebookTokenVerifier(new ClientFactory(handler), new());
        await Assert.ThrowsAsync<RequestFailureException>(() => verifier.VerifyAsync("token", "user", default).AsTask());
        Assert.Null(handler.RequestHost);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class ResponseHandler(string json) : HttpMessageHandler
    {
        public string? RequestHost { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { RequestHost = request.RequestUri!.Host; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }); }
    }
}
