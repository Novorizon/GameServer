using System.Security.Cryptography;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Security;
using Xunit;

namespace GameServer.UnitTests;

public sealed class PacketCipherTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static byte[] Packet(int size)
    {
        byte[] packet = new byte[28 + size];
        PacketCodec.WriteHeader(packet, new(1000, 99, 0, size));
        for (int i = 0; i < size; i++) packet[28 + i] = (byte)(i + 1);
        return packet;
    }
    [Fact]
    public void MatchesIndependentPythonHkdfAesGcmGoldenVector()
    {
        byte[] hello = Convert.FromHexString("5443504101000000000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
        using var cipher = PacketCipher.Create(Key, hello, server: false);
        byte[] wire = new byte[47];
        cipher.Encode(Packet(3), wire);
        Assert.Equal("4757533102000000E80300006300000000000000000000000300000083A82A174C3E95FCA639C7AD2BC621F367FC01", Convert.ToHexString(wire));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(1024)] [InlineData(32000)]
    public void IndependentDirectionsRoundtripInPlace(int size)
    {
        byte[] hello = PacketCipher.CreateHello();
        using var server = PacketCipher.Create(Key, hello, true);
        using var client = PacketCipher.Create(Key, hello, false);
        byte[] plain = Packet(size), wire = new byte[plain.Length + 16];
        for (int i = 0; i < 3; i++)
        {
            client.Encode(plain, wire); server.Decode(wire, 262144);
            Assert.Equal(plain, wire[..plain.Length]);
            server.Encode(plain, wire); client.Decode(wire, 262144);
            Assert.Equal(plain, wire[..plain.Length]);
        }
    }
    [Theory]
    [InlineData(8)] [InlineData(28)] [InlineData(46)]
    public void HeaderCiphertextAndTagTamperingAreRejected(int position)
    {
        byte[] hello = PacketCipher.CreateHello();
        using var server = PacketCipher.Create(Key, hello, true);
        using var client = PacketCipher.Create(Key, hello, false);
        byte[] wire = new byte[47]; client.Encode(Packet(3), wire); wire[position] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => server.Decode(wire, 262144));
    }
    [Fact]
    public void ReplayWrongKeyAndCrossConnectionReplayAreRejected()
    {
        byte[] hello = PacketCipher.CreateHello();
        using var server = PacketCipher.Create(Key, hello, true);
        using var client = PacketCipher.Create(Key, hello, false);
        byte[] wire = new byte[47]; client.Encode(Packet(3), wire);
        server.Decode(wire.ToArray(), 262144);
        Assert.ThrowsAny<CryptographicException>(() => server.Decode(wire.ToArray(), 262144));
        using var otherConnection = PacketCipher.Create(Key, PacketCipher.CreateHello(), true);
        Assert.ThrowsAny<CryptographicException>(() => otherConnection.Decode(wire.ToArray(), 262144));
        using var wrongKey = PacketCipher.Create(RandomNumberGenerator.GetBytes(32), hello, true);
        Assert.ThrowsAny<CryptographicException>(() => wrongKey.Decode(wire.ToArray(), 262144));
    }
    [Fact]
    public void ReflectedServerPacketCannotAuthenticateAsClientPacket()
    {
        using var server = PacketCipher.Create(Key, PacketCipher.CreateHello(), true);
        byte[] wire = new byte[47]; server.Encode(Packet(3), wire);
        Assert.ThrowsAny<CryptographicException>(() => server.Decode(wire, 262144));
    }
    [Fact]
    public void InvalidInitializationAndSizesFailClosed()
    {
        byte[] hello = PacketCipher.CreateHello(); hello[4] = 99;
        Assert.Throws<CryptographicException>(() => PacketCipher.Create(Key, hello, true));
        using var client = PacketCipher.Create(Key, PacketCipher.CreateHello(), false);
        Assert.Throws<ArgumentException>(() => client.Encode(Packet(3), new byte[46]));
        Assert.Throws<CryptographicException>(() => client.Decode(new byte[20], 1024));
    }
}
