using GameServer.Protocol.Framing;
using Google.Protobuf;

namespace GameServer.Networking.Abstractions;

public interface IConnectionMessageSender
{
    ValueTask SendAsync(IConnection connection, int messageId, long requestId, ResultCode result,
        IMessage? message, CancellationToken ct);
}
