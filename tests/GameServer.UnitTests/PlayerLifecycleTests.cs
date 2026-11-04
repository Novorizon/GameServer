using System.Net;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Players;
using GameServer.Gameplay.Sessions;
using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Memory;
using GameServer.Networking.Tcp;
using GameServer.Protocol.Framing;
using Xunit;

namespace GameServer.UnitTests;

public sealed class PlayerLifecycleTests
{
    [Fact]
    public async Task ReplacementPreservesContextAndRejectsQueuedOldSessionWork()
    {
        using var pools = new BufferPools(new TcpServerOptions()); using var metrics = new GameplayMetrics();
        var sessions = new SessionManager(metrics); var players = new PlayerManager(sessions, metrics);
        await using var oldConnection = new FakeConnection(1); await using var newConnection = new FakeConnection(2);
        var sender = new ConnectionMessageSender(new TcpServerOptions(), pools);
        var old = new GameSession(oldConnection, sender); var current = new GameSession(newConnection, sender);
        sessions.Add(old); sessions.Add(current); players.Bind(old, new(42, "device"));
        var context = players.Get(old); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        Task<int> running = context.ExecuteAsync(old, async _ => { entered.SetResult(); await release.Task; return 1; }, default).AsTask();
        await entered.Task; bool staleExecuted = false;
        Task<int> queued = context.ExecuteAsync(old, _ => { staleExecuted = true; return ValueTask.FromResult(2); }, default).AsTask();
        players.Bind(current, new(42, "device")); players.Unbind(old); sessions.Remove(old);
        Assert.Equal(ConnectionState.Closed, oldConnection.State); Assert.Same(context, players.Get(current));
        Assert.Equal(1, metrics.Players); release.SetResult(); await running;
        var error = await Assert.ThrowsAsync<RequestFailureException>(() => queued); Assert.Equal(ResultCode.SessionReplaced, error.Result); Assert.False(staleExecuted);
        Assert.Equal(3, await context.ExecuteAsync(current, _ => ValueTask.FromResult(3), default));
        players.Unbind(current); sessions.Remove(current); Assert.Equal(0, metrics.Players); Assert.Equal(0, metrics.Sessions);
    }

    private sealed class FakeConnection(long id) : IConnection
    {
        private readonly CancellationTokenSource _closed = new();
        public long Id => id; public EndPoint? RemoteEndPoint => null;
        public ConnectionState State => _closed.IsCancellationRequested ? ConnectionState.Closed : ConnectionState.Open;
        public CancellationToken Closed => _closed.Token;
        public ValueTask<ReceivedPacket?> ReceiveAsync(CancellationToken ct) => ValueTask.FromResult<ReceivedPacket?>(null);
        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct) => ValueTask.CompletedTask;
        public void Abort(DisconnectReason reason) => _closed.Cancel();
        public ValueTask DisposeAsync() { _closed.Dispose(); return ValueTask.CompletedTask; }
    }
}
