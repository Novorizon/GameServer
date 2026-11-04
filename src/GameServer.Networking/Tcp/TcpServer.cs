using GameServer.Networking.Security;
using GameServer.Networking.Memory;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Diagnostics;
using GameServer.Networking.Limits;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameServer.Networking.Tcp;

public sealed class TcpServer : IHostedService, IDisposable
{
    private readonly TcpServerOptions _options;
    private readonly IConnectionHandler _handler;
    private readonly TcpPacketReader _reader;
    private readonly ConnectionAdmissionController _admission;
    private readonly NetworkMetrics _metrics;
    private readonly ILogger<TcpServer> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SemaphoreSlim _slots;
    private readonly BufferPools _buffers;
    private readonly ConnectionCipherFactory _ciphers;
    private readonly CancellationTokenSource _acceptStop = new(), _forceStop = new();
    private readonly ConcurrentDictionary<long, ActiveConnection> _clients = [];
    private Task _acceptTask = Task.CompletedTask;
    private TcpListener? _listener;
    private long _nextId;
    private int _stopped;
    private sealed class ActiveConnection(Socket socket)
    {
        public Socket Socket { get; } = socket; public Task Task { get; set; } = Task.CompletedTask;
    }

    public TcpServer(TcpServerOptions options, IConnectionHandler handler, TcpPacketReader reader,
        ConnectionAdmissionController admission, NetworkMetrics metrics, ILogger<TcpServer> logger, IHostApplicationLifetime lifetime, BufferPools buffers, ConnectionCipherFactory ciphers)
    {
        options.Validate();
        _options = options;
        _handler = handler;
        _reader = reader;
        _admission = admission;
        _metrics = metrics;
        _logger = logger;
        _lifetime = lifetime;
        _slots = new(options.MaxConnections, options.MaxConnections);
        _buffers = buffers;
        _ciphers = ciphers;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = IPAddress.Parse(_options.ListenAddress);
        _listener = new(address, _options.Port);
        if (address.Equals(IPAddress.IPv6Any))
            _listener.Server.DualMode = true;
        _listener.Start(_options.Backlog);
        _acceptTask = AcceptLoopAsync();
        _logger.LogInformation("TCP listening on {Address}:{Port}; Encryption={Encryption}", _options.ListenAddress, _options.Port, _options.Encryption.Mode);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_acceptStop.IsCancellationRequested)
            {
                Socket socket = await _listener!.AcceptSocketAsync(_acceptStop.Token);
                socket.NoDelay = true;
                if (!_admission.TryAdmitRate())
                {
                    _metrics.Rejected("rate");
                    socket.Dispose();
                    continue;
                }
                if (!_slots.Wait(0))
                {
                    _metrics.Rejected("connections");
                    socket.Dispose();
                    continue;
                }
                long id = Interlocked.Increment(ref _nextId);
                var active = new ActiveConnection(socket);
                _clients[id] = active;
                _metrics.Accepted();
                active.Task = RunConnectionAsync(id, active);
            }
        }
        catch (Exception ex) when (_acceptStop.IsCancellationRequested && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception ex)
        {
            _logger.LogCritical("TCP accept loop failed ({ExceptionType})", ex.GetType().Name);
            _forceStop.Cancel();
            _lifetime.StopApplication();
            throw;
        }
    }

    private async Task RunConnectionAsync(long id, ActiveConnection active)
    {
        try
        {
            var network = new NetworkStream(active.Socket, ownsSocket: true);
            Stream stream = network;
            try
            {
                using var cipher = _ciphers.Enabled ? await InitializeCipherAsync(stream) : null;
                await using var connection = new TcpConnection(id, active.Socket, stream, _reader, _options, _admission, _metrics, cipher, _buffers);
                await _handler.HandleAsync(connection, _forceStop.Token);
            }
            finally { await stream.DisposeAsync(); }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug("TCP connection {ConnectionId} ended ({ExceptionType})", id, ex.GetType().Name);
        }
        catch (Exception ex) { _logger.LogError("TCP connection {ConnectionId} failed ({ExceptionType})", id, ex.GetType().Name); }
        finally { active.Socket.Dispose(); _clients.TryRemove(id, out _); _metrics.Closed(); _slots.Release(); }
    }

    private async ValueTask<GameServer.Protocol.Security.PacketCipher?> InitializeCipherAsync(Stream stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_forceStop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.SendTimeoutSeconds));
        return await _ciphers.AcceptAsync(stream, timeout.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;
        _acceptStop.Cancel();
        _listener?.Stop();
        try
        {
            await _acceptTask;
        }
        catch (Exception ex) { _logger.LogDebug("Accept task ended ({ExceptionType})", ex.GetType().Name); }
        _handler.BeginDrain();
        Task pending = Task.WhenAll(_clients.Values.Select(x => x.Task));
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(_options.DrainTimeoutSeconds), cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            _forceStop.Cancel();
            foreach (var active in _clients.Values)
                active.Socket.Dispose();
            try
            {
                await pending.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        _acceptStop.Cancel();
        _forceStop.Cancel();
        _listener?.Stop();
        foreach (var active in _clients.Values)
            active.Socket.Dispose();
    }
}
