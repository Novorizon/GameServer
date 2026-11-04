using System.Net;
using System.Net.Sockets;
using GameServer.Protocol.Security;
using GameServer.Networking.Memory;
using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Diagnostics;
using GameServer.Networking.Limits;

namespace GameServer.Networking.Tcp;

public sealed class TcpConnection : IConnection
{
    private readonly Socket _socket;
    private readonly Stream _stream;
    private readonly TcpPacketReader _reader;
    private readonly TcpServerOptions _options;
    private readonly NetworkMetrics _metrics;
    private readonly ByteBudget _globalPending, _localPending;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _closed = new();
    private readonly PacketCipher? _cipher;
    private readonly BufferPools? _buffers;
    private int _state, _disposed, _pendingSends;
    public long Id
    {
        get;
    }
    public EndPoint? RemoteEndPoint
    {
        get;
    }
    public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);
    public CancellationToken Closed => _closed.Token;
    public DisconnectReason? CloseReason
    {
        get; private set;
    }

    public TcpConnection(long id, Socket socket, Stream stream, TcpPacketReader reader,
        TcpServerOptions options, ConnectionAdmissionController admission, NetworkMetrics metrics, PacketCipher? cipher = null, BufferPools? buffers = null)
    {
        _cipher = cipher; _buffers = buffers;
        if (cipher != null && buffers == null) throw new ArgumentException("Encrypted connections require buffer pools.");
        Id = id;
        _socket = socket;
        _stream = stream;
        _reader = reader;
        _options = options;
        _metrics = metrics;
        RemoteEndPoint = socket.RemoteEndPoint;
        _globalPending = admission.PendingSendBytes;
        _localPending = new(options.PerConnectionPendingSendBytes);
    }

    public async ValueTask<ReceivedPacket?> ReceiveAsync(CancellationToken cancellationToken)
    {
        ReceivedPacket? packet = await _reader.ReadAsync(_stream, cancellationToken, _cipher);
        if (packet != null)
            _metrics.Received();
        return packet;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        int wireLength = checked(packet.Length + (_cipher == null ? 0 : PacketCipher.TagBytes));
        if (packet.Length < Protocol.Framing.PacketCodec.HeaderSize || wireLength > _options.MaxPacketBytes)
            throw new ArgumentOutOfRangeException(nameof(packet));
        if (Interlocked.Increment(ref _pendingSends) > _options.MaxPendingSendsPerConnection)
        {
            Interlocked.Decrement(ref _pendingSends);
            throw new SendBudgetExceededException();
        }
        bool local = false, global = false, locked = false;
        BufferLease? encrypted = null;
        try
        {
            if (!(local = _localPending.TryReserve(wireLength)) || !(global = _globalPending.TryReserve(wireLength)))
                throw new SendBudgetExceededException();
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Closed);
            operation.CancelAfter(TimeSpan.FromSeconds(_options.SendTimeoutSeconds));
            await _sendLock.WaitAsync(operation.Token);
            locked = true;
            ReadOnlyMemory<byte> wire = packet;
            if (_cipher != null)
            {
                encrypted = _buffers!.Send.Rent(wireLength);
                _cipher.Encode(packet.Span, encrypted.Memory.Span);
                wire = encrypted.Memory[..wireLength];
            }
            int written = 0;
            while (written < wire.Length)
            {
                int count = await _socket.SendAsync(wire[written..], SocketFlags.None, operation.Token);
                if (count == 0) throw new IOException("Connection closed during send.");
                written += count;
            }
            _metrics.Sent();
        }
        catch { Abort(DisconnectReason.ApplicationError); throw; }
        finally
        {
            encrypted?.Dispose();
            if (locked)
                _sendLock.Release();
            if (global)
                _globalPending.Release(wireLength);
            if (local)
                _localPending.Release(wireLength);
            Interlocked.Decrement(ref _pendingSends);
        }
    }

    public void Abort(DisconnectReason reason)
    {
        if (Interlocked.Exchange(ref _state, (int)ConnectionState.Closed) != (int)ConnectionState.Open)
            return;
        CloseReason = reason;
        try
        {
            _closed.Cancel();
        }
        catch (ObjectDisposedException) { }
        finally { _socket.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Abort(DisconnectReason.RemoteClosed);
        await _stream.DisposeAsync();
        _closed.Dispose();
    }
}
