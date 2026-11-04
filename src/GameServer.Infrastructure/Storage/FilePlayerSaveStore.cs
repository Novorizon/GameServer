using System.Security.Cryptography;
using System.Text;
using GameServer.Gameplay.Dispatching;
using GameServer.Gameplay.Modules.PlayerSave;
using GameServer.Protocol.Framing;

namespace GameServer.Infrastructure.Storage;

public sealed class StorageOptions
{
    public string RootPath { get; set; } = "data/player-saves";
    public int MaxSaveBytes { get; set; } = 128 * 1024;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath) || MaxSaveBytes is < 1 or > 1024 * 1024)
            throw new InvalidOperationException("Invalid Storage configuration.");
    }
}

public sealed class FilePlayerSaveStore : IPlayerSaveStore
{
    private readonly StorageOptions _options;
    private readonly string _root;
    // Preserves the existing 16-byte MD5(payload + salt) file format. This is
    // corruption detection, not an authentication or anti-cheat signature.
    private static readonly byte[] Salt = Encoding.ASCII.GetBytes("SD*)-23SDc)=+");
    public FilePlayerSaveStore(StorageOptions options)
    {
        options.Validate();
        _options = options;
        _root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(_root);
    }
    private string FilePath(long playerId)
    {
        if (playerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(playerId));
        return Path.Combine(_root, (playerId % 1000).ToString(System.Globalization.CultureInfo.InvariantCulture), playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".dat");
    }
    private static byte[] Hash(ReadOnlySpan<byte> data)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hash.AppendData(data);
        hash.AppendData(Salt);
        return hash.GetHashAndReset();
    }
    public async ValueTask<byte[]?> ReadAsync(long playerId, CancellationToken ct)
    {
        string path = FilePath(playerId);
        FileStream file;
        try
        {
            file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        await using (file)
        {
            if (file.Length < 16 || file.Length > _options.MaxSaveBytes + 16)
                throw new RequestFailureException(ResultCode.InvalidRequest);
            byte[] expected = new byte[16];
            byte[] data = new byte[checked((int)file.Length - 16)];
            await file.ReadExactlyAsync(expected, ct);
            await file.ReadExactlyAsync(data, ct);
            if (!CryptographicOperations.FixedTimeEquals(expected, Hash(data)))
                throw new RequestFailureException(ResultCode.InvalidRequest);
            return data;
        }
    }
    public async ValueTask WriteAsync(long playerId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (data.Length > _options.MaxSaveBytes)
            throw new RequestFailureException(ResultCode.InvalidRequest);
        string path = FilePath(playerId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await file.WriteAsync(Hash(data.Span), ct);
                await file.WriteAsync(data, ct);
                await file.FlushAsync(ct);
                file.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
