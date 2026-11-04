namespace GameServer.Networking.Memory;

public interface IBufferPool
{
    BufferLease Rent(int minimumCapacity);
    long InUseBytes
    {
        get;
    }
    long RetainedBytes
    {
        get;
    }
}

public sealed class BufferLease : IDisposable
{
    private byte[]? _buffer;
    private readonly ManagedBufferPool _pool;
    internal BufferLease(ManagedBufferPool pool, byte[] buffer)
    {
        _pool = pool;
        _buffer = buffer;
    }
    public Memory<byte> Memory => (_buffer ?? throw new ObjectDisposedException(nameof(BufferLease))).AsMemory();
    public int Capacity => Memory.Length;
    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer != null)
            _pool.Return(buffer);
    }
}
