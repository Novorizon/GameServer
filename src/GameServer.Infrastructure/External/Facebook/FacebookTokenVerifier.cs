using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Modules.Facebook;
using GameServer.Protocol.Framing;

namespace GameServer.Infrastructure.External.Facebook;

public sealed class FacebookOptions
{
    public bool Enabled
    {
        get; set;
    }
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public void Validate()
    {
        if (Enabled && (string.IsNullOrWhiteSpace(AppId) || string.IsNullOrWhiteSpace(AppSecret)))
            throw new InvalidOperationException("Enabled Facebook verification requires app credentials.");
    }
}

public sealed class FacebookTokenVerifier(IHttpClientFactory clients, FacebookOptions options) : IFacebookTokenVerifier
{
    public async ValueTask<string?> VerifyAsync(string token, string expectedUserId, CancellationToken ct)
    {
        if (!options.Enabled)
            throw new RequestFailureException(ResultCode.Unsupported);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.facebook.com/debug_token?input_token=" + Uri.EscapeDataString(token));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AppId + "|" + options.AppSecret);
        using HttpClient client = clients.CreateClient("Facebook");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            throw new RequestFailureException(ResultCode.ServerBusy);
        if (!response.IsSuccessStatusCode)
            return null;
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        // Debug-token responses are small; cap the response before parsing.
        byte[] buffer = new byte[32769];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0)
                break;
            length += read;
        }
        if (length > 32768)
            throw new RequestFailureException(ResultCode.ServerBusy);
        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("is_valid", out var valid) || valid.ValueKind != JsonValueKind.True ||
                !data.TryGetProperty("app_id", out var app) || app.GetString() != options.AppId ||
                !data.TryGetProperty("user_id", out var user) || user.GetString() != expectedUserId ||
                !data.TryGetProperty("expires_at", out var expires) || expires.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return null;
            if (data.TryGetProperty("data_access_expires_at", out var access) && access.GetInt64() != 0 && access.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return null;
            return user.GetString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }
}
