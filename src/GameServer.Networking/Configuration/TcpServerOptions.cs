using System.Net;

namespace GameServer.Networking.Configuration;

public sealed class TcpServerOptions
{
    public string ListenAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 1236;
    public int Backlog { get; set; } = 512;
    public int MaxConnections { get; set; } = 8000;
    public int MaxNewConnectionsPerSecond { get; set; } = 1000;
    public int ConnectionBurst { get; set; } = 2000;
    public int PacketTimeoutSeconds { get; set; } = 10;
    public int SendTimeoutSeconds { get; set; } = 3;
    public int DrainTimeoutSeconds { get; set; } = 10;
    public int MaxPacketBytes { get; set; } = 256 * 1024;
    public bool UseZeroByteRead { get; set; } = true;
    public int InitialBufferBytes { get; set; } = 1024;
    public long ReceiveMemoryBytes { get; set; } = 64L * 1024 * 1024;
    public long SendMemoryBytes { get; set; } = 64L * 1024 * 1024;
    public long GlobalPendingSendBytes { get; set; } = 64L * 1024 * 1024;
    public long PerConnectionPendingSendBytes { get; set; } = 1024 * 1024;
    public int MaxPendingSendsPerConnection { get; set; } = 16;
    public int MaxRetainedBufferBytes { get; set; } = 16 * 1024;
    public int MaxRetainedBuffersPerBucket { get; set; } = 32;
    public PacketEncryptionOptions Encryption { get; set; } = new();
    public int MaximumApplicationPacketBytes => MaxPacketBytes - (Encryption.Mode == PacketEncryptionMode.AesGcm ? 16 : 0);

    public void Validate()
    {
        if (!IPAddress.TryParse(ListenAddress, out _) || Port is < 1 or > 65535 || Backlog < 1 ||
            MaxConnections < 1 || MaxNewConnectionsPerSecond < 1 || ConnectionBurst < 1 ||
            PacketTimeoutSeconds is < 1 or > 300 ||
            SendTimeoutSeconds is < 1 or > 60 || DrainTimeoutSeconds is < 1 or > 300 ||
            InitialBufferBytes < 256 || InitialBufferBytes > MaxPacketBytes ||
            (InitialBufferBytes & (InitialBufferBytes - 1)) != 0 ||
            MaxPacketBytes < 1024 || MaxPacketBytes > 4 * 1024 * 1024 || (MaxPacketBytes & (MaxPacketBytes - 1)) != 0 ||
            ReceiveMemoryBytes < MaxPacketBytes || SendMemoryBytes < MaxPacketBytes ||
            GlobalPendingSendBytes < MaxPacketBytes || PerConnectionPendingSendBytes < MaxPacketBytes ||
            MaxPendingSendsPerConnection < 1 || MaxRetainedBuffersPerBucket is < 0 or > 256 ||
            MaxRetainedBufferBytes < InitialBufferBytes || MaxRetainedBufferBytes > 64 * 1024)
            throw new InvalidOperationException("Invalid Networking resource limits.");
    }
}
