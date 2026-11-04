namespace GameServer.Networking.Configuration;

public enum PacketEncryptionMode { None, AesGcm }

public sealed class PacketEncryptionOptions
{
    public PacketEncryptionMode Mode { get; set; } = PacketEncryptionMode.AesGcm;
    public string KeyBase64 { get; set; } = "";
    public byte[] DecodeKey()
    {
        if (!Enum.IsDefined(Mode)) throw new InvalidOperationException("Unknown packet encryption mode.");
        if (Mode == PacketEncryptionMode.None) return [];
        if (!System.Security.Cryptography.AesGcm.IsSupported) throw new PlatformNotSupportedException("AES-GCM is not supported on this runtime.");
        byte[] key;
        try { key = Convert.FromBase64String(KeyBase64); }
        catch (FormatException) { throw new InvalidOperationException("Communication key must be base64."); }
        if (key.Length != 32) throw new InvalidOperationException("AES-GCM communication key requires exactly 32 bytes.");
        return key;
    }
}
