using System.Diagnostics.Metrics;

namespace GameServer.Networking.Diagnostics;

public sealed class NetworkMetrics : IDisposable
{
    private readonly Meter _meter = new("GameServer.Networking");
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _received;
    private readonly Counter<long> _sent;
    private long _active, _acceptedCount, _rejectedCount;
    public long Active => Interlocked.Read(ref _active);
    public long AcceptedCount => Interlocked.Read(ref _acceptedCount);
    public long RejectedCount => Interlocked.Read(ref _rejectedCount);
    public NetworkMetrics()
    {
        _accepted = _meter.CreateCounter<long>("gameserver.connections.accepted");
        _rejected = _meter.CreateCounter<long>("gameserver.connections.rejected");
        _received = _meter.CreateCounter<long>("gameserver.packets.received");
        _sent = _meter.CreateCounter<long>("gameserver.packets.sent");
        _meter.CreateObservableGauge("gameserver.connections.active", () => Active);
    }
    public void Accepted()
    {
        Interlocked.Increment(ref _active);
        Interlocked.Increment(ref _acceptedCount);
        _accepted.Add(1);
    }
    public void Closed() => Interlocked.Decrement(ref _active);
    public void Rejected(string reason)
    {
        Interlocked.Increment(ref _rejectedCount);
        _rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }
    public void Received() => _received.Add(1);
    public void Sent() => _sent.Add(1);
    public void Dispose() => _meter.Dispose();
}
