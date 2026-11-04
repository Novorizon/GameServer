using GameServer.Protocol.Security;
using System.Security.Cryptography;
using GameServer.Networking.Abstractions;
using GameServer.Networking.Configuration;
using GameServer.Networking.Memory;
using GameServer.Protocol.Framing;

namespace GameServer.Networking.Tcp;

public sealed class TcpPacketReader(TcpServerOptions options, BufferPools buffers)
{
    public async ValueTask<ReceivedPacket?> ReadAsync(Stream stream, CancellationToken cancellationToken, PacketCipher? cipher = null)
    {
        // A readiness probe borrows no application buffer. Only the following
        // non-empty read decides EOF; an empty-read zero return is not EOF.
        if (options.UseZeroByteRead)
        {
#pragma warning disable CA2022 // Readiness probe requests zero bytes, not an exact data read.
            await stream.ReadAsync(Memory<byte>.Empty, cancellationToken);
#pragma warning restore CA2022
        }
        BufferLease? lease = buffers.Receive.Rent(options.InitialBufferBytes);
        try
        {
            int first = await stream.ReadAsync(lease.Memory[..PacketCodec.HeaderSize], cancellationToken);
            if (first == 0)
                return null;
            // Only start the assembly deadline after the first byte, not on idle.
            using var assembly = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            assembly.CancelAfter(TimeSpan.FromSeconds(options.PacketTimeoutSeconds));
            await stream.ReadExactlyAsync(lease.Memory.Slice(first, PacketCodec.HeaderSize - first), assembly.Token);
            if (!PacketCodec.TryReadHeader(lease.Memory.Span, options.MaxPacketBytes - (cipher == null ? 0 : PacketCipher.TagBytes), out var header))
                throw new ProtocolException("Invalid GWS1 packet header.");
            int size = PacketCodec.HeaderSize + header.PayloadLength + (cipher == null ? 0 : PacketCipher.TagBytes);
            if (size > lease.Capacity)
            {
                // Preserve only the fixed header while releasing the small
                // lease, so one maximum frame fits a one-frame byte budget.
                Span<byte> savedHeader = stackalloc byte[PacketCodec.HeaderSize];
                lease.Memory.Span[..PacketCodec.HeaderSize].CopyTo(savedHeader);
                lease.Dispose();
                lease = null;
                lease = buffers.Receive.Rent(size);
                savedHeader.CopyTo(lease.Memory.Span);
            }
            await stream.ReadExactlyAsync(lease.Memory.Slice(PacketCodec.HeaderSize, size - PacketCodec.HeaderSize), assembly.Token);
            if (cipher != null)
            {
                try { cipher.Decode(lease.Memory.Span[..size], options.MaxPacketBytes); }
                catch (CryptographicException) { throw new ProtocolException("Packet authentication failed."); }
            }
            var packet = new ReceivedPacket(lease, header);
            lease = null;
            return packet;
        }
        finally { lease?.Dispose(); }
    }
}
