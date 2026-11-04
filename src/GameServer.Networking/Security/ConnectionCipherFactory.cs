using System.Security.Cryptography;
using GameServer.Networking.Configuration;
using GameServer.Protocol.Security;

namespace GameServer.Networking.Security;

public sealed class ConnectionCipherFactory : IDisposable
{
    private readonly byte[] _key;
    public bool Enabled => _key.Length != 0;
    public ConnectionCipherFactory(TcpServerOptions options) { _key = options.Encryption.DecodeKey(); }
    public async ValueTask<PacketCipher?> AcceptAsync(Stream stream, CancellationToken ct)
    {
        if (!Enabled) return null;
        byte[] hello = PacketCipher.CreateHello();
        var cipher = PacketCipher.Create(_key, hello, server: true);
        try { await stream.WriteAsync(hello, ct); return cipher; }
        catch { cipher.Dispose(); throw; }
    }
    public async ValueTask<PacketCipher?> ConnectAsync(Stream stream, CancellationToken ct)
    {
        if (!Enabled) return null;
        byte[] hello = new byte[PacketCipher.HelloBytes];
        await stream.ReadExactlyAsync(hello, ct);
        return PacketCipher.Create(_key, hello, server: false);
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
