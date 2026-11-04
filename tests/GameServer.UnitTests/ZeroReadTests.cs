using GameServer.Networking.Configuration;
using GameServer.Networking.Memory;
using GameServer.Networking.Tcp;
using GameServer.Protocol.Framing;
using Xunit;

namespace GameServer.UnitTests;

public sealed class ZeroReadTests
{
    [Fact]
    public async Task ZeroReadDoesNotBorrowBufferUntilReadyAndZeroResultIsNotEof()
    {
        var options = new TcpServerOptions { UseZeroByteRead = true };
        using var pools = new BufferPools(options);
        byte[] bytes = new byte[28];
        PacketCodec.WriteHeader(bytes, new(1, 7, 0, 0));
        using var stream = new ReadinessStream(bytes);
        var reader = new TcpPacketReader(options, pools);
        Task<GameServer.Networking.Abstractions.ReceivedPacket?> pending = reader.ReadAsync(stream, default).AsTask();
        Assert.Equal(1, stream.ZeroReads);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, pools.Receive.InUseBytes);
        stream.Ready.TrySetResult();
        using (var packet = await pending)
        {
            Assert.NotNull(packet);
            Assert.Equal(7, packet.Header.RequestId);
            Assert.True(pools.Receive.InUseBytes > 0);
        }
        Assert.Null(await reader.ReadAsync(stream, default));
        Assert.Equal(0, pools.Receive.InUseBytes);
    }

    [Fact]
    public async Task CancellationDuringReadinessDoesNotBorrowOrLeak()
    {
        var options = new TcpServerOptions { UseZeroByteRead = true };
        using var pools = new BufferPools(options);
        using var stream = new ReadinessStream([]);
        using var cancellation = new CancellationTokenSource();
        var pending = new TcpPacketReader(options, pools).ReadAsync(stream, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, pools.Receive.InUseBytes);
        Assert.Equal(0, pools.Receive.RetainedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsecutiveFragmentedPacketsAndTruncatedBodyRespectOwnership(bool zeroRead)
    {
        var options = new TcpServerOptions { UseZeroByteRead = zeroRead };
        using var pools = new BufferPools(options);
        byte[] bytes = new byte[56];
        PacketCodec.WriteHeader(bytes, new(1, 1, 0, 0));
        PacketCodec.WriteHeader(bytes.AsSpan(28), new(1, 2, 0, 16000));
        using var stream = new ReadinessStream(bytes);
        stream.Ready.TrySetResult();
        var reader = new TcpPacketReader(options, pools);
        using (var packet = await reader.ReadAsync(stream, default)) Assert.Equal(1, packet!.Header.RequestId);
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadAsync(stream, default).AsTask());
        Assert.Equal(0, pools.Receive.InUseBytes);
    }

    private sealed class ReadinessStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ZeroReads { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.IsEmpty)
            {
                ZeroReads++;
                await Ready.Task.WaitAsync(ct);
                return 0;
            }
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], ct);
        }
    }
}
