using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Memory;
using GameServer.Protocol.Framing;
using Google.Protobuf;

namespace GameServer.Networking.Tcp;

// Owns framing and the pooled plaintext buffer until the full send completes.
public sealed class ConnectionMessageSender(TcpServerOptions options, BufferPools buffers) : IConnectionMessageSender
{
    public async ValueTask SendAsync(IConnection connection, int messageId, long requestId, ResultCode result,
        IMessage? message, CancellationToken ct)
    {
        int length = checked(PacketCodec.HeaderSize + (message?.CalculateSize() ?? 0));
        if (length > options.MaximumApplicationPacketBytes)
            throw new ArgumentOutOfRangeException(nameof(message));
        using var lease = buffers.Send.Rent(length);
        PacketCodec.WriteMessage(lease.Memory.Span, messageId, requestId, result, message);
        await connection.SendAsync(lease.Memory[..length], ct);
    }
}
