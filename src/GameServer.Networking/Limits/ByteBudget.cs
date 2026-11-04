namespace GameServer.Networking.Limits;

public sealed class ByteBudget(long limit)
{
    private long _used;
    public long Used => Interlocked.Read(ref _used);
    public bool TryReserve(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        long current;
        do
        {
            current = Used;
            if (bytes > limit - current)
                return false;
        }
        while (Interlocked.CompareExchange(ref _used, current + bytes, current) != current);
        return true;
    }
    public void Release(long bytes) => Interlocked.Add(ref _used, -bytes);
}
