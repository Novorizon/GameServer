using System.Net;
using GameServer.Networking.Memory;
using GameServer.Protocol.Framing;

namespace GameServer.Networking.Abstractions;

public enum ConnectionState
{
    Open, Closed
}
public enum DisconnectReason
{
    RemoteClosed, ProtocolError, Timeout, ServerStopping, Replaced, Overloaded, ApplicationError, AdministrativeKick
}

public interface IConnection : IAsyncDisposable
{
    long Id
    {
        get;
    }
    EndPoint? RemoteEndPoint
    {
        get;
    }
    ConnectionState State
    {
        get;
    }
    CancellationToken Closed
    {
        get;
    }
    ValueTask<ReceivedPacket?> ReceiveAsync(CancellationToken cancellationToken);
    // Completion means all bytes were written or the connection was aborted.
    ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
    void Abort(DisconnectReason reason);
}

public interface IConnectionHandler
{
    Task HandleAsync(IConnection connection, CancellationToken forceStop);
    void BeginDrain();
}

public sealed class ReceivedPacket(BufferLease owner, PacketHeader header) : IDisposable
{
    public PacketHeader Header { get; } = header;
    public ReadOnlyMemory<byte> Payload => owner.Memory.Slice(PacketCodec.HeaderSize, Header.PayloadLength);
    public void Dispose() => owner.Dispose();
}

public sealed class ProtocolException(string message) : IOException(message);
public sealed class SendBudgetExceededException() : IOException("Send admission limit exceeded.");
