using System.Collections.Concurrent;
using System.Net;
using GameServer.Gameplay.Administration;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Notifications;
using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Memory;
using GameServer.Networking.Tcp;
using GameServer.Protocol.Framing;
using GameServer.Protocol.Messages;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.UnitTests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task PushTargetsCurrentSessionAndBroadcastDeduplicatesRecipients()
    {
        using var world = new World();
        var old = world.Add(1); var current = world.Add(1); world.Add(2);
        world.Players.Unbind(old); world.Sessions.Remove(old);
        using var notifier = world.Notifier();
        Assert.True(await notifier.NotifyAsync(1, MessageId.ServerNotice, new ServerNotice { Text = "current" }));
        Assert.Equal(current.Id, Assert.Single(world.Sender.Sent));
        Assert.False(await notifier.NotifyAsync(999, MessageId.ServerNotice, new ServerNotice()));
        world.Sender.Sent.Clear();
        var result = await notifier.NotifyManyAsync(new long[] { 1, 1, 2, 999 }, MessageId.ServerNotice, new ServerNotice());
        Assert.Equal(new NotificationBroadcastResult(3, 2, 1), result);
        Assert.Equal(2, world.Sender.Sent.Count);
        Assert.DoesNotContain(old.Id, world.Sender.Sent);
    }

    [Fact]
    public async Task FailedRecipientDoesNotPreventDeliveryToOtherPlayers()
    {
        using var world = new World();
        var failing = world.Add(1); world.Add(2);
        world.Sender.OnSend = (connection, _) => connection.Id == failing.Id
            ? ValueTask.FromException(new IOException("Closed peer")) : ValueTask.CompletedTask;
        using var notifier = world.Notifier();
        var result = await notifier.NotifyManyAsync(new long[] { 1, 2 }, MessageId.ServerNotice, new ServerNotice());
        Assert.Equal(new NotificationBroadcastResult(2, 1, 1), result);
    }

    [Fact]
    public async Task BroadcastLimitsConcurrencyRejectsExcessWorkAndRecoversAfterCancellation()
    {
        using var world = new World();
        for (int i = 1; i <= 4; i++) world.Add(i);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Sender.OnSend = async (_, ct) =>
        {
            if (world.Sender.Active == 2) entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        using var notifier = world.Notifier(new() { BroadcastConcurrency = 2, MaxConcurrentBroadcasts = 1, MaxBroadcastRecipients = 4 });
        using var cancellation = new CancellationTokenSource();
        var first = notifier.NotifyManyAsync(new long[] { 1, 2, 3, 4 }, MessageId.ServerNotice, new ServerNotice(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, world.Sender.Peak);
        var busy = await Assert.ThrowsAsync<RequestFailureException>(() =>
            notifier.NotifyManyAsync(new long[] { 1 }, MessageId.ServerNotice, new ServerNotice()));
        Assert.Equal(ResultCode.ServerBusy, busy.Result);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            notifier.NotifyManyAsync(new long[] { 1, 2, 3, 4, 5 }, MessageId.ServerNotice, new ServerNotice()));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(0, world.Sender.Active);
        world.Sender.OnSend = null;
        Assert.Equal(new NotificationBroadcastResult(1, 1, 0), await notifier.NotifyManyAsync(
            new long[] { 1 }, MessageId.ServerNotice, new ServerNotice()));
    }

    [Fact]
    public void KickClosesSelectedSessionWithoutClosingAConcurrentReplacement()
    {
        using var world = new World();
        var old = world.Add(1);
        GameSession? replacement = null;
        ((FakeConnection)old.Connection).OnAbort = () => replacement = world.Add(1);
        var admin = new PlayerAdministrationService(world.Players, NullLogger<PlayerAdministrationService>.Instance);
        Assert.True(admin.Kick(1));
        Assert.NotNull(replacement);
        world.Players.Unbind(old); world.Sessions.Remove(old);
        Assert.True(world.Players.TryGetCurrentSession(1, out var current));
        Assert.Same(replacement, current);
        Assert.Equal(ConnectionState.Open, current.Connection.State);
        Assert.Equal(DisconnectReason.AdministrativeKick, ((FakeConnection)old.Connection).Reason);
        Assert.True(admin.Kick(1));
        Assert.Equal(ConnectionState.Closed, current.Connection.State);
        Assert.False(admin.Kick(1));
        Assert.False(admin.Kick(999));
    }

    [Fact]
    public async Task MessageSenderKeepsLeaseUntilCompletionAndReleasesOnFailure()
    {
        var options = new TcpServerOptions();
        using var buffers = new BufferPools(options);
        using var connection = new FakeConnection(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ReadOnlyMemory<byte> pending = default;
        connection.OnSend = async (packet, ct) => { pending = packet; entered.SetResult(); await release.Task.WaitAsync(ct); };
        var sender = new ConnectionMessageSender(options, buffers);
        var send = sender.SendAsync(connection, (int)MessageId.ServerNotice, 0, ResultCode.Ok,
            new ServerNotice { Text = "pooled" }, default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(buffers.Send.InUseBytes > 0);
        Assert.True(PacketCodec.TryReadHeader(pending.Span, options.MaximumApplicationPacketBytes, out var header));
        Assert.True(PacketCodec.IsNotification(header));
        Assert.Equal("pooled", ServerNotice.Parser.ParseFrom(pending.Span[28..]).Text);
        release.SetResult(); await send;
        Assert.Equal(0, buffers.Send.InUseBytes);
        Assert.True(pending.Span.IndexOfAnyExcept((byte)0) < 0);
        connection.OnSend = (_, _) => ValueTask.FromException(new IOException("Closed peer"));
        await Assert.ThrowsAsync<IOException>(() => sender.SendAsync(connection, (int)MessageId.ServerNotice,
            0, ResultCode.Ok, new ServerNotice(), default).AsTask());
        Assert.Equal(0, buffers.Send.InUseBytes);
    }

    private sealed class World : IDisposable
    {
        public GameplayMetrics Metrics { get; } = new();
        public SessionManager Sessions { get; }
        public PlayerManager Players { get; }
        public RecordingSender Sender { get; } = new();
        private readonly List<FakeConnection> _connections = [];
        public World() { Sessions = new(Metrics); Players = new(Sessions, Metrics); }
        public GameSession Add(long playerId)
        {
            var connection = new FakeConnection(_connections.Count + 1); _connections.Add(connection);
            var session = new GameSession(connection, Sender); Sessions.Add(session);
            Players.Bind(session, new(playerId, "device-" + playerId));
            return session;
        }
        public PlayerNotificationService Notifier(NotificationOptions? options = null) =>
            new(Players, options ?? new(), NullLogger<PlayerNotificationService>.Instance);
        public void Dispose() { foreach (var connection in _connections) connection.Dispose(); Metrics.Dispose(); }
    }

    private sealed class RecordingSender : IConnectionMessageSender
    {
        public ConcurrentQueue<long> Sent { get; } = new();
        public Func<IConnection, CancellationToken, ValueTask>? OnSend;
        public int Active, Peak;
        public async ValueTask SendAsync(IConnection connection, int messageId, long requestId, ResultCode result, IMessage? message, CancellationToken ct)
        {
            Sent.Enqueue(connection.Id);
            int active = Interlocked.Increment(ref Active);
            int peak;
            do { peak = Volatile.Read(ref Peak); } while (active > peak && Interlocked.CompareExchange(ref Peak, active, peak) != peak);
            try { if (OnSend != null) await OnSend(connection, ct); }
            finally { Interlocked.Decrement(ref Active); }
        }
    }

    private sealed class FakeConnection(long id) : IConnection, IDisposable
    {
        private readonly CancellationTokenSource _closed = new();
        private int _stopped;
        public long Id => id;
        public EndPoint? RemoteEndPoint => null;
        public ConnectionState State => Volatile.Read(ref _stopped) == 0 ? ConnectionState.Open : ConnectionState.Closed;
        public CancellationToken Closed => _closed.Token;
        public Action? OnAbort;
        public DisconnectReason? Reason;
        public Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? OnSend;
        public ValueTask<ReceivedPacket?> ReceiveAsync(CancellationToken ct) => ValueTask.FromResult<ReceivedPacket?>(null);
        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct) => OnSend?.Invoke(packet, ct) ?? ValueTask.CompletedTask;
        public void Abort(DisconnectReason reason)
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            Reason = reason; _closed.Cancel(); OnAbort?.Invoke();
        }
        public void Dispose() => _closed.Dispose();
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
