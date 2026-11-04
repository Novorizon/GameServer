using System.Security.Cryptography;
using GameServer.Gameplay.Administration;
using GameServer.Gameplay.Notifications;
using GameServer.LoadTests;
using GameServer.Networking.Configuration;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class NotificationIntegrationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PushInterleavesWithRepliesBroadcastsAndFollowsCurrentSession(bool encrypted)
    {
        string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var mode = encrypted ? PacketEncryptionMode.AesGcm : PacketEncryptionMode.None;
        await using var server = await TestServer.StartAsync(new()
        { ["Networking:Encryption:Mode"] = mode.ToString(), ["Networking:Encryption:KeyBase64"] = key });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var firstSocket = await server.ConnectAsync();
        using var first = await ClientPacketTransport.ConnectAsync(firstSocket.GetStream(), mode, key, timeout.Token);
        await first.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.Login, 42, server.Login(8060)), timeout.Token);
        var notifier = server.Host.Services.GetRequiredService<IPlayerNotifier>();
        var admin = server.Host.Services.GetRequiredService<IPlayerAdministrationService>();
        var notices = new List<string>();
        first.NotificationReceived += (header, body) =>
        {
            Assert.Equal(0, header.RequestId);
            notices.Add(ServerNotice.Parser.ParseFrom(body.Span).Text);
        };
        Assert.True(await notifier.NotifyAsync(8060, MessageId.ServerNotice, new ServerNotice { Text = "idle" }, timeout.Token));
        await first.ReadNotificationAsync(timeout.Token);
        Assert.Equal((int)MessageId.ServerNotice, first.Header.MessageId);
        Assert.True(await notifier.NotifyAsync(8060, MessageId.ServerNotice, new ServerNotice { Text = "before-reply" }, timeout.Token));
        await first.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.Heartbeat, 43, null), timeout.Token);
        Assert.Equal(43, first.Header.RequestId);
        Assert.Equal(new[] { "idle", "before-reply" }, notices);
        var result = await notifier.NotifyManyAsync(new long[] { 8060, 8060, 999999 }, MessageId.ServerNotice,
            new ServerNotice { Text = "broadcast" }, timeout.Token);
        Assert.Equal(new NotificationBroadcastResult(2, 1, 1), result);
        await first.ReadNotificationAsync(timeout.Token);
        Assert.Equal("broadcast", notices[^1]);

        using var replacementSocket = await server.ConnectAsync();
        using var replacement = await ClientPacketTransport.ConnectAsync(replacementSocket.GetStream(), mode, key, timeout.Token);
        await replacement.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.Login, 44, server.Login(8060)), timeout.Token);
        Assert.True(await notifier.NotifyAsync(8060, MessageId.ServerNotice, new ServerNotice { Text = "current-only" }, timeout.Token));
        await replacement.ReadNotificationAsync(timeout.Token);
        Assert.Equal("current-only", ServerNotice.Parser.ParseFrom(replacement.Body.Span).Text);
        await Assert.ThrowsAnyAsync<IOException>(() => first.ReadNotificationAsync(timeout.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(admin.Kick(8060));
        await Assert.ThrowsAnyAsync<IOException>(() => replacement.ReadNotificationAsync(timeout.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        await server.AssertReleasedAsync();
        Assert.False(admin.Kick(8060));
        Assert.False(await notifier.NotifyAsync(8060, MessageId.ServerNotice, new ServerNotice(), timeout.Token));

        // Kick disconnects a session; it is not an account ban.
        using var reconnectSocket = await server.ConnectAsync();
        using var reconnect = await ClientPacketTransport.ConnectAsync(reconnectSocket.GetStream(), mode, key, timeout.Token);
        await reconnect.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.Login, 45, server.Login(8060)), timeout.Token);
        reconnectSocket.Close(); await server.AssertReleasedAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClientCannotInvokeServerNotificationAsARequest(bool encrypted)
    {
        string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var mode = encrypted ? PacketEncryptionMode.AesGcm : PacketEncryptionMode.None;
        await using var server = await TestServer.StartAsync(new()
        { ["Networking:Encryption:Mode"] = mode.ToString(), ["Networking:Encryption:KeyBase64"] = key });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await server.ConnectAsync();
        using var transport = await ClientPacketTransport.ConnectAsync(socket.GetStream(), mode, key, timeout.Token);
        await transport.ExchangeAsync(TcpIntegrationTests.Encode((int)MessageId.Login, 1, server.Login(8061)), timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => transport.ExchangeAsync(TcpIntegrationTests.Encode(
            (int)MessageId.ServerNotice, 2, new ServerNotice { Text = "client injection" }), timeout.Token).AsTask());
        await server.AssertReleasedAsync();
    }
}
