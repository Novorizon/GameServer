using System.Buffers.Binary;
using Google.Protobuf;

namespace GameServer.Protocol.Framing;

public static class PacketCodec
{
    public const int HeaderSize = 28;
    public const uint Magic = 0x31535747;
    public const byte Version = 2;
    public const int NotificationMessageIdStart = 2100000000;
    public const long NotificationRequestId = 0;

    public static bool IsNotificationMessageId(int messageId) => messageId >= NotificationMessageIdStart;
    public static bool IsNotification(PacketHeader header) => IsNotificationMessageId(header.MessageId) &&
        header.RequestId == NotificationRequestId && header.ResultCode == ResultCode.Ok;

    public static bool TryReadHeader(ReadOnlySpan<byte> data, int maxPacketBytes, out PacketHeader header)
    {
        header = default;
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic ||
            data[4] != Version || data[5] != 0 || data[6] != 0 || data[7] != 0)
            return false;
        int length = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
        if (length < 0 || length > maxPacketBytes - HeaderSize)
            return false;
        header = new(BinaryPrimitives.ReadInt32LittleEndian(data[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(data[12..]),
            (ResultCode)BinaryPrimitives.ReadInt32LittleEndian(data[20..]), length);
        return true;
    }

    public static void WriteHeader(Span<byte> target, PacketHeader header)
    {
        if (target.Length < HeaderSize || header.PayloadLength < 0)
            throw new ArgumentException("Invalid packet size.");
        BinaryPrimitives.WriteUInt32LittleEndian(target, Magic);
        target[4] = Version;
        target.Slice(5, 3).Clear();
        BinaryPrimitives.WriteInt32LittleEndian(target[8..], header.MessageId);
        BinaryPrimitives.WriteInt64LittleEndian(target[12..], header.RequestId);
        BinaryPrimitives.WriteInt32LittleEndian(target[20..], (int)header.ResultCode);
        BinaryPrimitives.WriteInt32LittleEndian(target[24..], header.PayloadLength);
    }

    public static int WriteMessage(Span<byte> target, int messageId, long requestId, ResultCode result, IMessage? body)
    {
        int length = body?.CalculateSize() ?? 0;
        if (target.Length < checked(HeaderSize + length))
            throw new ArgumentException("Packet buffer is too small.");
        WriteHeader(target, new(messageId, requestId, result, length));
        body?.WriteTo(target.Slice(HeaderSize, length));
        return HeaderSize + length;
    }
}
