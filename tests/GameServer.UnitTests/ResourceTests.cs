using System.Buffers.Binary;
using GameServer.Protocol.Framing;
using GameServer.Networking.Configuration;
using GameServer.Networking.Memory;
using GameServer.Networking.Tcp;
using GameServer.Networking.Abstractions;
using GameServer.Gameplay.Configuration;
using GameServer.Gameplay.Diagnostics;
using GameServer.Gameplay.Dispatching;
using Xunit;

namespace GameServer.UnitTests;

public sealed class ResourceTests
{
    [Fact]
    public void HeaderMatchesIndependentGoldenVector()
    {
        byte[] bytes = new byte[28];
        PacketCodec.WriteHeader(bytes, new(1000, 0x0102030405060708, ResultCode.InvalidRequest, 3));
        Assert.Equal("4757533102000000E803000008070605040302010100000003000000", Convert.ToHexString(bytes));
        Assert.True(PacketCodec.TryReadHeader(bytes, 1024, out var header));
        Assert.Equal(3, header.PayloadLength);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(997)]
    public void InvalidLengthsRejectedBeforeRentingBody(int length)
    {
        byte[] bytes = new byte[28]; PacketCodec.WriteHeader(bytes, new(1, 1, 0, 0));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), length);
        Assert.False(PacketCodec.TryReadHeader(bytes, 1024, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void InvalidHeaderRejected(int offset)
    {
        byte[] bytes = new byte[28]; PacketCodec.WriteHeader(bytes, new(1, 1, 0, 0)); bytes[offset] ^= 1;
        Assert.False(PacketCodec.TryReadHeader(bytes, 1024, out _));
    }

    [Fact]
    public void PoolEnforcesBudgetClearsDataAndBoundsRetention()
    {
        using var pool = new ManagedBufferPool(1024, 1024, 256, 1);
        var a = pool.Rent(1); var b = pool.Rent(1); var c = pool.Rent(512);
        a.Memory.Span.Fill(123);
        Assert.Throws<BufferBudgetExceededException>(() => pool.Rent(1));
        a.Dispose(); a.Dispose(); b.Dispose(); c.Dispose();
        Assert.Equal(0, pool.InUseBytes); Assert.Equal(256, pool.RetainedBytes);
        using var reused = pool.Rent(1); Assert.True(reused.Memory.Span.IndexOfAnyExcept((byte)0) < 0);
        Assert.Throws<ObjectDisposedException>(() => _ = a.Memory);
    }

    [Fact]
    public void PoolDisposalAllowsOutstandingLeaseReturn()
    {
        var pool = new ManagedBufferPool(1024, 1024, 1024, 1);
        var lease = pool.Rent(512); pool.Dispose(); lease.Dispose();
        Assert.Equal(0, pool.InUseBytes); Assert.Equal(0, pool.RetainedBytes);
        Assert.Throws<ObjectDisposedException>(() => pool.Rent(1));
        Assert.Equal(0, pool.InUseBytes);
    }

    [Fact]
    public async Task ReaderHandlesFragmentationAndConsecutivePackets()
    {
        var options = new TcpServerOptions(); using var pools = new BufferPools(options);
        var reader = new TcpPacketReader(options, pools);
        byte[] bytes = new byte[28 + 1500 + 28];
        PacketCodec.WriteHeader(bytes, new(1000, 5, 0, 1500));
        bytes.AsSpan(28, 1500).Fill(42); PacketCodec.WriteHeader(bytes.AsSpan(1528), new(1, 6, 0, 0));
        using var stream = new FragmentStream(bytes);
        using (var first = await reader.ReadAsync(stream, default))
        { Assert.Equal(5, first!.Header.RequestId); Assert.Equal(1500, first.Payload.Length); Assert.Equal(42, first.Payload.Span[1499]); }
        using (var second = await reader.ReadAsync(stream, default)) Assert.Equal(6, second!.Header.RequestId);
        Assert.Null(await reader.ReadAsync(stream, default)); Assert.Equal(0, pools.Receive.InUseBytes);
    }

    [Fact]
    public async Task TruncatedBodyReturnsLease()
    {
        var options = new TcpServerOptions(); using var pools = new BufferPools(options);
        var reader = new TcpPacketReader(options, pools); byte[] bytes = new byte[28];
        PacketCodec.WriteHeader(bytes, new(1000, 1, 0, 2000));
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await reader.ReadAsync(new MemoryStream(bytes), default));
        Assert.Equal(0, pools.Receive.InUseBytes);
    }

    [Fact]
    public async Task QueuesAreBoundedAndLoginLaneIsIndependent()
    {
        var options = new GameplayOptions { RequestConcurrency = 1, RequestQueueCapacity = 1, LoginConcurrency = 1, LoginQueueCapacity = 1 };
        using var metrics = new GameplayMetrics(); var admission = new RequestAdmissionController(options, metrics);
        await admission.StartAsync(default);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        Task first = admission.ExecuteAsync(false, async ct => { entered.SetResult(); await release.Task; }, default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task queued = admission.ExecuteAsync(false, _ => ValueTask.CompletedTask, default).AsTask();
        var busy = await Assert.ThrowsAsync<RequestFailureException>(() => admission.ExecuteAsync(false, _ => ValueTask.CompletedTask, default).AsTask());
        Assert.Equal(ResultCode.ServerBusy, busy.Result);
        await admission.ExecuteAsync(true, _ => ValueTask.CompletedTask, default);
        release.SetResult(); await Task.WhenAll(first, queued);
        await admission.StopAsync(default); Assert.Equal(0, metrics.Queued); Assert.Equal(0, metrics.Running);
    }

    [Fact]
    public async Task CancellationDoesNotReturnWhileWorkerStillOwnsPacket()
    {
        using var metrics = new GameplayMetrics(); var admission = new RequestAdmissionController(new GameplayOptions { RequestConcurrency = 1 }, metrics);
        await admission.StartAsync(default); using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        Task task = admission.ExecuteAsync(false, async _ => { entered.SetResult(); await release.Task; }, cts.Token).AsTask();
        await entered.Task; cts.Cancel(); Assert.False(task.IsCompleted);
        release.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); await admission.StopAsync(default);
    }

    private sealed class FragmentStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], ct);
    }
}
