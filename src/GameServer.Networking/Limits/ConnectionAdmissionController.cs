using System.Diagnostics;
using GameServer.Networking.Configuration;

namespace GameServer.Networking.Limits;

public sealed class ConnectionAdmissionController
{
    private readonly object _sync = new();
    private readonly TcpServerOptions _options;
    private double _tokens;
    private long _last = Stopwatch.GetTimestamp();
    public ByteBudget PendingSendBytes
    {
        get;
    }
    public ConnectionAdmissionController(TcpServerOptions options)
    {
        _options = options;
        _tokens = options.ConnectionBurst;
        PendingSendBytes = new(options.GlobalPendingSendBytes);
    }
    public bool TryAdmitRate()
    {
        lock (_sync)
        {
            long now = Stopwatch.GetTimestamp();
            _tokens = Math.Min(_options.ConnectionBurst, _tokens + (double)(now - _last) / Stopwatch.Frequency * _options.MaxNewConnectionsPerSecond);
            _last = now;
            if (_tokens < 1)
                return false;
            _tokens--;
            return true;
        }
    }
}
