using System.Security.Cryptography;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using GameServer.Protocol.Security;
using Google.Protobuf;
using Xunit;

namespace GameServer.IntegrationTests;

public sealed class EncryptionTests
{
    [Fact]
    public async Task DefaultEncryptionRequiresAKeyWithoutFallingBackToPlaintext()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TestServer.StartAsync(useDefaultEncryption: true));
        Assert.Contains("communication key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EncryptedLoginFragmentationCoalescingAndBusinessRoundtrip(bool zeroRead)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        await using var server = await StartAsync(key, zeroRead);
        using var client = await server.ConnectAsync();
        var stream = client.GetStream();
        using var cipher = await ReadCipherAsync(stream, key);
        byte[] login = Encode(cipher, MessageId.Login, server.Login(8050));
        for (int offset = 0; offset < login.Length; offset += 3)
            await stream.WriteAsync(login.AsMemory(offset, Math.Min(3, login.Length - offset)));
        var reply = await ReadAsync(stream, cipher);
        Assert.Equal(8050, LoginResponse.Parser.ParseFrom(reply.Body).PlayerId);
        byte[] first = Encode(cipher, MessageId.Heartbeat, null), second = Encode(cipher, MessageId.Heartbeat, null);
        await stream.WriteAsync(first.Concat(second).ToArray());
        Assert.Empty((await ReadAsync(stream, cipher)).Body); Assert.Empty((await ReadAsync(stream, cipher)).Body);
        byte[] data = RandomNumberGenerator.GetBytes(1024);
        await stream.WriteAsync(Encode(cipher, MessageId.SaveWrite, new SaveWriteRequest { Data = ByteString.CopyFrom(data) }));
        Assert.Equal(ResultCode.Ok, (await ReadAsync(stream, cipher)).Header.ResultCode);
        await stream.WriteAsync(Encode(cipher, MessageId.SaveRead, new SaveReadRequest()));
        Assert.Equal(data, SaveResponse.Parser.ParseFrom((await ReadAsync(stream, cipher)).Body).Data.ToByteArray());
        client.Close(); await server.AssertReleasedAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WrongKeyOrModifiedTagClosesBeforeLoginAndReturnsBuffers(bool wrongKey)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        await using var server = await StartAsync(key);
        using var client = await server.ConnectAsync();
        using var cipher = await ReadCipherAsync(client.GetStream(), wrongKey ? RandomNumberGenerator.GetBytes(32) : key);
        byte[] packet = Encode(cipher, MessageId.Login, server.Login(8051));
        if (!wrongKey) packet[^1] ^= 1;
        await client.GetStream().WriteAsync(packet);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        await server.AssertReleasedAsync();
    }

    [Fact]
    public async Task ReplayingAnAuthenticatedFrameClosesTheConnection()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        await using var server = await StartAsync(key);
        using var client = await server.ConnectAsync();
        using var cipher = await ReadCipherAsync(client.GetStream(), key);
        byte[] login = Encode(cipher, MessageId.Login, server.Login(8052));
        await client.GetStream().WriteAsync(login); await ReadAsync(client.GetStream(), cipher);
        await client.GetStream().WriteAsync(login);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        await server.AssertReleasedAsync();
    }

    [Fact]
    public async Task MissingKeyFailsBeforeListening()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestServer.StartAsync(new()
        { ["Networking:Encryption:Mode"] = "AesGcm", ["Networking:Encryption:KeyBase64"] = "" }));
    }

    [Fact]
    public async Task BenchmarkRouteIsUnavailableInProduction()
    {
        await using var server = await TestServer.StartAsync(); using var client = await server.ConnectAsync();
        await TcpIntegrationTests.RequestAsync(client.GetStream(), MessageId.Login, server.Login(8053));
        await client.GetStream().WriteAsync(TcpIntegrationTests.Encode(1900000001, 1, new SaveWriteRequest()));
        Assert.Equal(ResultCode.Unsupported, (await TcpIntegrationTests.ReadAsync(client.GetStream())).Header.ResultCode);
        await server.AssertReleasedAsync();
    }

    private static Task<TestServer> StartAsync(byte[] key, bool zeroRead = true) => TestServer.StartAsync(new()
    { ["Networking:Encryption:Mode"] = "AesGcm", ["Networking:Encryption:KeyBase64"] = Convert.ToBase64String(key), ["Networking:UseZeroByteRead"] = zeroRead.ToString() });
    private static async Task<PacketCipher> ReadCipherAsync(Stream stream, byte[] key)
    {
        byte[] hello = new byte[40]; await stream.ReadExactlyAsync(hello);
        return PacketCipher.Create(key, hello, false);
    }
    private static byte[] Encode(PacketCipher cipher, MessageId id, IMessage? message)
    {
        byte[] plain = TcpIntegrationTests.Encode((int)id, 1, message), wire = new byte[plain.Length + 16];
        cipher.Encode(plain, wire); return wire;
    }
    internal static async Task<(PacketHeader Header, byte[] Body)> ReadAsync(Stream stream, PacketCipher cipher)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] head = new byte[28]; await stream.ReadExactlyAsync(head, timeout.Token);
        Assert.True(PacketCodec.TryReadHeader(head, 262144 - 16, out var header));
        byte[] wire = new byte[28 + header.PayloadLength + 16]; head.CopyTo(wire, 0);
        await stream.ReadExactlyAsync(wire.AsMemory(28), timeout.Token); cipher.Decode(wire, 262144);
        Assert.Equal(ResultCode.Ok, header.ResultCode);
        return (header, wire.AsSpan(28, header.PayloadLength).ToArray());
    }
}
