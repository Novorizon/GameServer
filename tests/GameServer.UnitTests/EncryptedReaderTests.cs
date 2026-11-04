using System.Security.Cryptography;
using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Memory;
using GameServer.Networking.Tcp;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Security;
using Xunit;

namespace GameServer.UnitTests;

public sealed class EncryptedReaderTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MaximumFrameFitsAnExactSingleFrameReceiveBudget(bool encrypted)
    {
        var options = new TcpServerOptions { ReceiveMemoryBytes = 256 * 1024 };
        using var buffers = new BufferPools(options);
        byte[] key = RandomNumberGenerator.GetBytes(32), hello = PacketCipher.CreateHello();
        using var send = encrypted ? PacketCipher.Create(key, hello, false) : null;
        using var receive = encrypted ? PacketCipher.Create(key, hello, true) : null;
        byte[] plain = new byte[options.MaxPacketBytes - (encrypted ? 16 : 0)];
        PacketCodec.WriteHeader(plain, new(1, 1, 0, plain.Length - 28));
        plain.AsSpan(28).Fill(19);
        byte[] wire = encrypted ? new byte[options.MaxPacketBytes] : plain;
        send?.Encode(plain, wire);
        using (var packet = await new TcpPacketReader(options, buffers).ReadAsync(new MemoryStream(wire), default, receive))
        {
            Assert.Equal(plain.Length - 28, packet!.Payload.Length);
            Assert.True(packet.Payload.Span.IndexOfAnyExcept((byte)19) < 0);
            Assert.Equal(options.ReceiveMemoryBytes, buffers.Receive.InUseBytes);
        }
        Assert.Equal(0, buffers.Receive.InUseBytes);
    }

    [Fact]
    public async Task MaximumWirePacketFitsBudgetAndDecryptsInPlace()
    {
        var options = new TcpServerOptions(); using var buffers = new BufferPools(options);
        byte[] key = RandomNumberGenerator.GetBytes(32), hello = PacketCipher.CreateHello();
        using var send = PacketCipher.Create(key, hello, false); using var receive = PacketCipher.Create(key, hello, true);
        byte[] plain = new byte[options.MaxPacketBytes - 16];
        PacketCodec.WriteHeader(plain, new(1, 1, 0, plain.Length - 28));
        plain.AsSpan(28).Fill(17);
        byte[] wire = new byte[options.MaxPacketBytes]; send.Encode(plain, wire);
        using (var packet = await new TcpPacketReader(options, buffers).ReadAsync(new MemoryStream(wire), default, receive))
        {
            Assert.Equal(plain.Length - 28, packet!.Payload.Length);
            Assert.True(packet.Payload.Span.IndexOfAnyExcept((byte)17) < 0);
            Assert.Equal(options.MaxPacketBytes, buffers.Receive.InUseBytes);
        }
        Assert.Equal(0, buffers.Receive.InUseBytes);
    }

    [Fact]
    public async Task OversizeEncryptedHeaderFailsBeforeBodyRental()
    {
        var options = new TcpServerOptions(); using var buffers = new BufferPools(options);
        using var cipher = PacketCipher.Create(RandomNumberGenerator.GetBytes(32), PacketCipher.CreateHello(), true);
        byte[] header = new byte[28]; PacketCodec.WriteHeader(header, new(1, 1, 0, options.MaxPacketBytes - 28 - 16 + 1));
        await Assert.ThrowsAsync<ProtocolException>(() => new TcpPacketReader(options, buffers).ReadAsync(new MemoryStream(header), default, cipher).AsTask());
        Assert.Equal(0, buffers.Receive.InUseBytes);
        Assert.True(buffers.Receive.RetainedBytes <= options.InitialBufferBytes);
    }
}
