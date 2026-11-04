using System.Threading.Channels;
using GameServer.Gameplay.Configuration;
using GameServer.Gameplay.Diagnostics;
using GameServer.Protocol.Framing;
using Microsoft.Extensions.Hosting;

namespace GameServer.Gameplay.Dispatching;

public sealed class RequestAdmissionController : IHostedService
{
    private readonly Lane _login, _normal;
    private readonly GameplayOptions _options;
    private readonly GameplayMetrics _metrics;
    private int _draining;
    private sealed record Work(Func<CancellationToken, ValueTask> Action, CancellationTokenSource Operation,
        TaskCompletionSource Completion);
    private sealed class Lane(int capacity, int workers)
    {
        public Channel<Work> Queue
        {
            get;
        } = Channel.CreateBounded<Work>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = workers == 1,
            AllowSynchronousContinuations = false
        });
        public int Workers { get; } = workers;
        public Task[] Tasks { get; set; } = [];
    }
    public RequestAdmissionController(GameplayOptions options, GameplayMetrics metrics)
    {
        options.Validate();
        _options = options;
        _metrics = metrics;
        _login = new(options.LoginQueueCapacity, options.LoginConcurrency);
        _normal = new(options.RequestQueueCapacity, options.RequestConcurrency);
    }
    public Task StartAsync(CancellationToken ct)
    {
        foreach (var lane in new[] { _login, _normal })
            lane.Tasks = Enumerable.Range(0, lane.Workers).Select(_ => RunLaneAsync(lane)).ToArray();
        return Task.CompletedTask;
    }
    public async ValueTask ExecuteAsync(bool login, Func<CancellationToken, ValueTask> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _draining) != 0)
        {
            _metrics.Reject();
            throw new RequestFailureException(ResultCode.ServerBusy);
        }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        operation.CancelAfter(TimeSpan.FromSeconds(_options.QueueTimeoutSeconds));
        var work = new Work(action, operation, new(TaskCreationOptions.RunContinuationsAsynchronously));
        var lane = login ? _login : _normal;
        _metrics.Queue(1);
        if (!lane.Queue.Writer.TryWrite(work))
        {
            _metrics.Queue(-1);
            _metrics.Reject();
            throw new RequestFailureException(ResultCode.ServerBusy);
        }
        // Never abandon this completion on cancellation: the caller owns packet
        // memory until the worker has definitively stopped accessing it.
        await work.Completion.Task;
    }
    private async Task RunLaneAsync(Lane lane)
    {
        await foreach (var work in lane.Queue.Reader.ReadAllAsync())
        {
            _metrics.Queue(-1);
            if (work.Operation.IsCancellationRequested)
            {
                work.Completion.TrySetCanceled(work.Operation.Token);
                continue;
            }
            _metrics.Run(1);
            try
            {
                work.Operation.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
                await work.Action(work.Operation.Token);
                work.Operation.Token.ThrowIfCancellationRequested();
                work.Completion.TrySetResult();
                _metrics.Complete();
            }
            catch (OperationCanceledException) { work.Completion.TrySetCanceled(work.Operation.Token); }
            catch (Exception ex) { work.Completion.TrySetException(ex); }
            finally { _metrics.Run(-1); }
        }
    }
    public void BeginDrain()
    {
        Interlocked.Exchange(ref _draining, 1);
        _login.Queue.Writer.TryComplete();
        _normal.Queue.Writer.TryComplete();
    }
    public async Task StopAsync(CancellationToken ct)
    {
        BeginDrain();
        await Task.WhenAll(_login.Tasks.Concat(_normal.Tasks)).WaitAsync(ct);
    }
}
