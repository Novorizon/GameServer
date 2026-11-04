using System.Collections.Frozen;
using Google.Protobuf;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Abstractions;
using GameServer.Protocol.Framing;
using Microsoft.Extensions.Logging;

namespace GameServer.Gameplay.Dispatching;

public interface IMessageHandler
{
    int Id
    {
        get;
    }
    bool RequiresAuthentication
    {
        get;
    }
    ValueTask HandleAsync(GameSession session, PacketHeader header, ReadOnlyMemory<byte> payload, CancellationToken ct);
}

public abstract class MessageHandler<T>(int id, MessageParser<T> parser) : IMessageHandler where T : class, IMessage<T>
{
    public int Id => id;
    public virtual bool RequiresAuthentication => true;
    public ValueTask HandleAsync(GameSession session, PacketHeader header, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        T message;
        try
        {
            message = parser.ParseFrom(payload.Span);
        }
        catch (InvalidProtocolBufferException) { throw new RequestFailureException(ResultCode.InvalidRequest); }
        return HandleMessageAsync(session, header, message, ct);
    }
    protected abstract ValueTask HandleMessageAsync(GameSession session, PacketHeader header, T message, CancellationToken ct);
}

public sealed class PacketDispatcher
{
    private readonly FrozenDictionary<int, IMessageHandler> _handlers;
    private readonly RequestAdmissionController _admission;
    private readonly ILogger<PacketDispatcher> _logger;
    public PacketDispatcher(IEnumerable<IMessageHandler> handlers, RequestAdmissionController admission, ILogger<PacketDispatcher> logger)
    {
        _handlers = handlers.ToFrozenDictionary(x => x.Id);
        _admission = admission;
        _logger = logger;
    }
    public async ValueTask<bool> DispatchAsync(GameSession session, ReceivedPacket packet, CancellationToken ct)
    {
        var header = packet.Header;
        if (header.ResultCode != ResultCode.Ok)
            throw new ProtocolException("Client result code must be zero.");
        if (!_handlers.TryGetValue(header.MessageId, out var handler))
        {
            await session.SendAsync(header, ResultCode.Unsupported, null, ct);
            session.Close(DisconnectReason.ProtocolError);
            return false;
        }
        if (handler.RequiresAuthentication && session.State != SessionState.Authenticated)
        {
            await session.SendAsync(header, ResultCode.NotAuthenticated, null, ct);
            session.Close(DisconnectReason.ProtocolError);
            return false;
        }
        try
        {
            await _admission.ExecuteAsync(header.MessageId == 1000,
                token => handler.HandleAsync(session, header, packet.Payload, token), ct);
            return true;
        }
        catch (RequestFailureException ex) { await session.SendAsync(header, ex.Result, null, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await session.SendAsync(header, ResultCode.RequestTimeout, null, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError("Handler {MessageId} failed ({ExceptionType})", header.MessageId, ex.GetType().Name);
            await session.SendAsync(header, ResultCode.InternalError, null, ct);
        }
        return false;
    }
}
