using GameServer.Networking.Configuration;

namespace GameServer.Networking.Memory;

public sealed class BufferPools : IDisposable
{
    public ManagedBufferPool Receive
    {
        get;
    }
    public ManagedBufferPool Send
    {
        get;
    }
    public BufferPools(TcpServerOptions options)
    {
        options.Validate();
        Receive = new(options.ReceiveMemoryBytes, options.MaxPacketBytes, options.MaxRetainedBufferBytes, options.MaxRetainedBuffersPerBucket);
        Send = new(options.SendMemoryBytes, options.MaxPacketBytes, options.MaxRetainedBufferBytes, options.MaxRetainedBuffersPerBucket);
    }
    public void Dispose()
    {
        Receive.Dispose();
        Send.Dispose();
    }
}
