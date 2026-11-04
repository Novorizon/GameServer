using System.Buffers.Binary;
using System.Security.Cryptography;
using GameServer.Protocol.Framing;

namespace GameServer.Protocol.Security;

// One connection, two independent directions. Caller serializes each direction.
// Keys are derived from a pre-shared communication key and fresh server salt.
public sealed class PacketCipher : IDisposable
{
    public const int TagBytes = 16;
    public const int HelloBytes = 40;
    private readonly AesGcm _send, _receive;
    private ulong _sendCounter, _receiveCounter;

    private PacketCipher(AesGcm send, AesGcm receive) { _send = send; _receive = receive; }

    public static byte[] CreateHello()
    {
        byte[] hello = new byte[HelloBytes];
        "TCPA"u8.CopyTo(hello);
        hello[4] = 1;
        RandomNumberGenerator.Fill(hello.AsSpan(8));
        return hello;
    }

    public static PacketCipher Create(ReadOnlySpan<byte> key, ReadOnlySpan<byte> hello, bool server)
    {
        if (key.Length != 32 || hello.Length != HelloBytes || !hello[..4].SequenceEqual("TCPA"u8) ||
            hello[4] != 1 || hello[5] != 0 || hello[6] != 0 || hello[7] != 0)
            throw new CryptographicException("Invalid encryption initialization.");
        Span<byte> c2s = stackalloc byte[32];
        Span<byte> s2c = stackalloc byte[32];
        AesGcm? send = null;
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, key, c2s, hello[8..], "GameTcp/v1/client-to-server"u8);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, key, s2c, hello[8..], "GameTcp/v1/server-to-client"u8);
            send = new AesGcm(server ? s2c : c2s, TagBytes);
            return new(send, new AesGcm(server ? c2s : s2c, TagBytes));
        }
        catch { send?.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(c2s); CryptographicOperations.ZeroMemory(s2c); }
    }

    public int Encode(ReadOnlySpan<byte> plaintextPacket, Span<byte> destination)
    {
        if (_sendCounter == ulong.MaxValue) throw new CryptographicException("Connection message counter exhausted.");
        if (!PacketCodec.TryReadHeader(plaintextPacket, plaintextPacket.Length, out var header) ||
            plaintextPacket.Length != PacketCodec.HeaderSize + header.PayloadLength || destination.Length < plaintextPacket.Length + TagBytes)
            throw new ArgumentException("Invalid plaintext packet.");
        Span<byte> nonce = stackalloc byte[12];
        nonce.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], _sendCounter);
        plaintextPacket[..28].CopyTo(destination);
        _send.Encrypt(nonce, plaintextPacket[28..], destination.Slice(28, header.PayloadLength),
            destination.Slice(plaintextPacket.Length, TagBytes), destination[..28]);
        _sendCounter++;
        return plaintextPacket.Length + TagBytes;
    }

    public void Decode(Span<byte> wirePacket, int maximumWireBytes)
    {
        if (_receiveCounter == ulong.MaxValue) throw new CryptographicException("Connection message counter exhausted.");
        if (!PacketCodec.TryReadHeader(wirePacket, maximumWireBytes - TagBytes, out var header) ||
            wirePacket.Length != 28 + header.PayloadLength + TagBytes)
            throw new CryptographicException("Invalid encrypted packet size.");
        Span<byte> nonce = stackalloc byte[12];
        nonce.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], _receiveCounter);
        // AesGcm supports in-place decryption. Authenticate the visible header too.
        Span<byte> body = wirePacket.Slice(28, header.PayloadLength);
        _receive.Decrypt(nonce, body, wirePacket.Slice(28 + header.PayloadLength, TagBytes), body, wirePacket[..28]);
        _receiveCounter++;
    }

    public void Dispose() { _send.Dispose(); _receive.Dispose(); }
}
