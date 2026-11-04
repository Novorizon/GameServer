using System.Diagnostics.Metrics;

namespace GameServer.Gameplay.Diagnostics;

public sealed class GameplayMetrics : IDisposable
{
    private readonly Meter _meter = new("GameServer.Gameplay");
    private long _sessions, _players, _queued, _running, _busy, _completed;
    public long Sessions => Interlocked.Read(ref _sessions);
    public long Players => Interlocked.Read(ref _players);
    public long Queued => Interlocked.Read(ref _queued);
    public long Running => Interlocked.Read(ref _running);
    public long Busy => Interlocked.Read(ref _busy);
    public long Completed => Interlocked.Read(ref _completed);
    public GameplayMetrics()
    {
        _meter.CreateObservableGauge("gameserver.sessions", () => Sessions);
        _meter.CreateObservableGauge("gameserver.players", () => Players);
        _meter.CreateObservableGauge("gameserver.requests.queued", () => Queued);
        _meter.CreateObservableGauge("gameserver.requests.running", () => Running);
        _meter.CreateObservableCounter("gameserver.requests.busy", () => Busy);
        _meter.CreateObservableCounter("gameserver.requests.completed", () => Completed);
    }
    public void Session(int delta) => Interlocked.Add(ref _sessions, delta);
    public void Player(int delta) => Interlocked.Add(ref _players, delta);
    public void Queue(int delta) => Interlocked.Add(ref _queued, delta);
    public void Run(int delta) => Interlocked.Add(ref _running, delta);
    public void Reject() => Interlocked.Increment(ref _busy);
    public void Complete() => Interlocked.Increment(ref _completed);
    public void Dispose() => _meter.Dispose();
}
