using System.Net;
using System.Security.Cryptography;
using GameServer.Protocol.Security;
using System.Net.Sockets;
using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Diagnostics;
using GameServer.Networking.Limits;
using GameServer.Networking.Memory;
using GameServer.Networking.Tcp;
using GameServer.Protocol.Framing;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class SendOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSendsWriteWholePacketsWithoutInterleaving(bool encrypted)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient(); var connecting = client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using Socket socket = await listener.AcceptSocketAsync(); await connecting; listener.Stop();
        socket.SendBufferSize = 1024;
        var options = new TcpServerOptions(); using var buffers = new BufferPools(options); using var metrics = new NetworkMetrics();
        var admission = new ConnectionAdmissionController(options);
        byte[] hello = PacketCipher.CreateHello(), key = RandomNumberGenerator.GetBytes(32);
        using var sendCipher = encrypted ? PacketCipher.Create(key, hello, true) : null;
        using var receiveCipher = encrypted ? PacketCipher.Create(key, hello, false) : null;
        await using var connection = new TcpConnection(1, socket, new NetworkStream(socket), new(options, buffers), options, admission, metrics, sendCipher, buffers);
        var packets = Enumerable.Range(1, 3).Select(i =>
        { byte[] packet = new byte[128 * 1024]; packet.AsSpan(28).Fill((byte)i); PacketCodec.WriteHeader(packet, new(1, i, 0, packet.Length - 28)); return packet; }).ToArray();
        Task sending = Task.WhenAll(packets.Select(p => connection.SendAsync(p, default).AsTask()));
        var ids = new HashSet<long>();
        for (int i = 0; i < 3; i++)
        {
            var packet = receiveCipher == null ? await TcpIntegrationTests.ReadAsync(client.GetStream()) : await EncryptionTests.ReadAsync(client.GetStream(), receiveCipher); ids.Add(packet.Header.RequestId);
            Assert.True(packet.Body.AsSpan().IndexOfAnyExcept((byte)packet.Header.RequestId) < 0);
        }
        await sending; Assert.Equal(3, ids.Count); Assert.Equal(0, admission.PendingSendBytes.Used);
    }

    [Fact]
    public async Task BlockedSendBoundsPendingOperationsAndCancellationReleasesBudgets()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient { ReceiveBufferSize = 1024 };
        var connecting = client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using Socket socket = await listener.AcceptSocketAsync(); await connecting; listener.Stop(); socket.SendBufferSize = 1024;
        var options = new TcpServerOptions { MaxPacketBytes = 4 * 1024 * 1024, PerConnectionPendingSendBytes = 8 * 1024 * 1024, MaxPendingSendsPerConnection = 2 };
        using var buffers = new BufferPools(options); using var metrics = new NetworkMetrics(); var admission = new ConnectionAdmissionController(options);
        await using var connection = new TcpConnection(1, socket, new NetworkStream(socket), new(options, buffers), options, admission, metrics);
        using var cancel = new CancellationTokenSource(); byte[] packet = new byte[4 * 1024 * 1024];
        Task first = connection.SendAsync(packet, cancel.Token).AsTask(); Task second = connection.SendAsync(packet, cancel.Token).AsTask();
        await Assert.ThrowsAsync<SendBudgetExceededException>(() => connection.SendAsync(packet, default).AsTask());
        Assert.Equal(8 * 1024 * 1024, admission.PendingSendBytes.Used); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(0, admission.PendingSendBytes.Used); Assert.Equal(ConnectionState.Closed, connection.State);
    }
}
