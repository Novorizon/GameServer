using System.Numerics;
using GameServer.Networking.Limits;

namespace GameServer.Networking.Memory;

public sealed class ManagedBufferPool : IBufferPool, IDisposable
{
    private readonly ByteBudget _budget;
    private readonly int _maximum;
    private readonly int _retainedMaximum;
    private readonly int _perBucket;
    private readonly object _sync = new();
    private readonly Dictionary<int, Stack<byte[]>> _buckets = [];
    private bool _disposed;
    private long _retained;
    public long InUseBytes => _budget.Used;
    public long RetainedBytes
    {
        get
        {
            lock (_sync)
                return _retained;
        }
    }

    public ManagedBufferPool(long budget, int maximum, int retainedMaximum, int perBucket)
    {
        if (budget < maximum || maximum < 256 || (maximum & (maximum - 1)) != 0 || perBucket < 0)
            throw new ArgumentOutOfRangeException(nameof(budget));
        _budget = new(budget);
        _maximum = maximum;
        _retainedMaximum = retainedMaximum;
        _perBucket = perBucket;
    }

    public BufferLease Rent(int minimumCapacity)
    {
        if (minimumCapacity < 1 || minimumCapacity > _maximum)
            throw new ArgumentOutOfRangeException(nameof(minimumCapacity));
        int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(256, minimumCapacity));
        if (!_budget.TryReserve(capacity))
            throw new BufferBudgetExceededException();
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_buckets.TryGetValue(capacity, out var stack) && stack.TryPop(out byte[]? buffer))
                {
                    _retained -= capacity;
                    return new(this, buffer);
                }
            }
            return new(this, GC.AllocateUninitializedArray<byte>(capacity));
        }
        catch { _budget.Release(capacity); throw; }
    }

    internal void Return(byte[] buffer)
    {
        Array.Clear(buffer);
        lock (_sync)
        {
            if (!_disposed && buffer.Length <= _retainedMaximum && _perBucket > 0)
            {
                if (!_buckets.TryGetValue(buffer.Length, out var stack))
                    _buckets.Add(buffer.Length, stack = new(_perBucket));
                if (stack.Count < _perBucket)
                {
                    stack.Push(buffer);
                    _retained += buffer.Length;
                }
            }
        }
        _budget.Release(buffer.Length);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _buckets.Clear();
            _retained = 0;
        }
    }
}

public sealed class BufferBudgetExceededException() : IOException("Managed buffer budget exhausted.");
