using GameServer.Networking.Configuration;
using GameServer.Networking.Security;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Security;
using System.Buffers.Binary;

namespace GameServer.LoadTests;

// Small sample/test adapter. The caller owns the stream; this owns only cipher
// state and reusable frame buffers. One outstanding request per connection.
public sealed class ClientPacketTransport(Stream stream, PacketCipher? cipher) : IDisposable
{
    private readonly byte[] _receive = new byte[4096], _send = new byte[4096];
    public PacketHeader Header { get; private set; }
    public ReadOnlyMemory<byte> Body => _receive.AsMemory(28, Header.PayloadLength);
    // Payload memory is reused by the next read. Copy it if it must be retained.
    public event Action<PacketHeader, ReadOnlyMemory<byte>>? NotificationReceived;

    public static ValueTask<ClientPacketTransport> ConnectAsync(Stream stream, PacketEncryptionMode mode, CancellationToken ct) =>
        ConnectAsync(stream, mode, Environment.GetEnvironmentVariable("GAMESERVER_Networking__Encryption__KeyBase64") ?? "", ct);

    public static async ValueTask<ClientPacketTransport> ConnectAsync(Stream stream, PacketEncryptionMode mode, string keyBase64, CancellationToken ct)
    {
        var options = new TcpServerOptions();
        options.Encryption.Mode = mode;
        options.Encryption.KeyBase64 = keyBase64;
        using var factory = new ConnectionCipherFactory(options);
        return new(stream, await factory.ConnectAsync(stream, ct));
    }

    public async ValueTask ExchangeAsync(byte[] packet, CancellationToken ct)
    {
        if (cipher == null) await stream.WriteAsync(packet, ct);
        else
        {
            int size = cipher.Encode(packet, _send);
            await stream.WriteAsync(_send.AsMemory(0, size), ct);
        }
        while (true)
        {
            await ReadFrameAsync(ct);
            if (PacketCodec.IsNotification(Header))
            {
                NotificationReceived?.Invoke(Header, Body);
                continue;
            }
            if (Header.ResultCode != ResultCode.Ok || Header.MessageId != BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8)) ||
                Header.RequestId != BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(12)))
                throw new IOException("Response result/correlation mismatch.");
            return;
        }
    }

    // Use only when no ExchangeAsync/read is outstanding on this connection.
    public async ValueTask ReadNotificationAsync(CancellationToken ct)
    {
        await ReadFrameAsync(ct);
        if (!PacketCodec.IsNotification(Header)) throw new IOException("Expected a server notification.");
        NotificationReceived?.Invoke(Header, Body);
    }

    private async ValueTask ReadFrameAsync(CancellationToken ct)
    {
        await stream.ReadExactlyAsync(_receive.AsMemory(0, 28), ct);
        int overhead = cipher == null ? 0 : PacketCipher.TagBytes;
        if (!PacketCodec.TryReadHeader(_receive, 262144 - overhead, out var header) || header.PayloadLength > _receive.Length - 28 - overhead)
            throw new IOException("Invalid response size.");
        await stream.ReadExactlyAsync(_receive.AsMemory(28, header.PayloadLength + overhead), ct);
        cipher?.Decode(_receive.AsSpan(0, 28 + header.PayloadLength + overhead), 262144);
        Header = header;
    }

    public void Dispose() => cipher?.Dispose();
}
